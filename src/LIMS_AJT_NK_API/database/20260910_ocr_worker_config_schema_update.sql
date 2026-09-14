SET XACT_ABORT ON;

BEGIN TRANSACTION;

IF COL_LENGTH('dbo.t_interface_lims_ocr_config_api', 'input_ocr_bearer_token') IS NULL
    ALTER TABLE dbo.t_interface_lims_ocr_config_api
        ADD input_ocr_bearer_token NVARCHAR(2000) NULL;

IF COL_LENGTH('dbo.t_interface_lims_ocr_config_api', 'update_master_url') IS NULL
    ALTER TABLE dbo.t_interface_lims_ocr_config_api
        ADD update_master_url NVARCHAR(500) NULL;

IF COL_LENGTH('dbo.t_interface_lims_ocr_config_api', 'get_result_ocr_url') IS NULL
    ALTER TABLE dbo.t_interface_lims_ocr_config_api
        ADD get_result_ocr_url NVARCHAR(500) NULL;

IF COL_LENGTH('dbo.t_interface_lims_ocr_config_api', 'feedback_url') IS NULL
    ALTER TABLE dbo.t_interface_lims_ocr_config_api
        ADD feedback_url NVARCHAR(500) NULL;

IF COL_LENGTH('dbo.t_interface_lims_ocr_config_api', 'document_host_directory') IS NULL
    ALTER TABLE dbo.t_interface_lims_ocr_config_api
        ADD document_host_directory NVARCHAR(500) NULL;

IF COL_LENGTH('dbo.t_interface_lims_ocr_config_api', 'document_web_path') IS NULL
    ALTER TABLE dbo.t_interface_lims_ocr_config_api
        ADD document_web_path NVARCHAR(500) NULL;

IF COL_LENGTH('dbo.t_interface_lims_ocr_config_api', 'document_group') IS NULL
    ALTER TABLE dbo.t_interface_lims_ocr_config_api
        ADD document_group NVARCHAR(64) NULL;

IF COL_LENGTH('dbo.t_interface_lims_ocr_config_api', 'file_stable_seconds') IS NULL
    ALTER TABLE dbo.t_interface_lims_ocr_config_api
        ADD file_stable_seconds INT NOT NULL
            CONSTRAINT DF_lims_ocr_config_file_stable_seconds DEFAULT (5) WITH VALUES;

IF COL_LENGTH('dbo.t_interface_lims_ocr_config_api', 'max_send_attempts') IS NULL
    ALTER TABLE dbo.t_interface_lims_ocr_config_api
        ADD max_send_attempts INT NOT NULL
            CONSTRAINT DF_lims_ocr_config_max_send_attempts DEFAULT (3) WITH VALUES;

IF COL_LENGTH('dbo.t_interface_lims_ocr_config_api', 'retry_delay_seconds') IS NULL
    ALTER TABLE dbo.t_interface_lims_ocr_config_api
        ADD retry_delay_seconds INT NOT NULL
            CONSTRAINT DF_lims_ocr_config_retry_delay_seconds DEFAULT (5) WITH VALUES;

IF COL_LENGTH('dbo.t_interface_lims_ocr_config_api', 'request_timeout_seconds') IS NULL
    ALTER TABLE dbo.t_interface_lims_ocr_config_api
        ADD request_timeout_seconds INT NOT NULL
            CONSTRAINT DF_lims_ocr_config_request_timeout_seconds DEFAULT (60) WITH VALUES;

DECLARE @config_id UNIQUEIDENTIFIER =
(
    SELECT TOP (1) config_id
    FROM dbo.t_interface_lims_ocr_config_api
    WHERE is_enabled = 1
    ORDER BY create_date DESC
);

IF @config_id IS NOT NULL
BEGIN
    DECLARE @input_ocr_url NVARCHAR(500) =
    (
        SELECT input_ocr_url
        FROM dbo.t_interface_lims_ocr_config_api
        WHERE config_id = @config_id
    );

    DECLARE @aji_base_url NVARCHAR(500) =
        CASE
            WHEN @input_ocr_url LIKE '%/input_ocr'
                THEN LEFT(@input_ocr_url, LEN(@input_ocr_url) - LEN('/input_ocr'))
            ELSE NULL
        END;

    UPDATE dbo.t_interface_lims_ocr_config_api
    SET submission_mode = N'file',
        input_ocr_file_url = @input_ocr_url,
        input_ocr_file_field_name = N'files',
        update_master_url = COALESCE(update_master_url, @aji_base_url + N'/update_master_data'),
        get_result_ocr_url = COALESCE(get_result_ocr_url, @aji_base_url + N'/get_result_ocr'),
        feedback_url = COALESCE(feedback_url, @aji_base_url + N'/feedback'),
        file_stable_seconds = CASE WHEN file_stable_seconds <= 0 THEN 5 ELSE file_stable_seconds END,
        max_send_attempts = CASE WHEN max_send_attempts <= 0 THEN 3 ELSE max_send_attempts END,
        retry_delay_seconds = CASE WHEN retry_delay_seconds < 0 THEN 5 ELSE retry_delay_seconds END,
        request_timeout_seconds = CASE WHEN request_timeout_seconds <= 0 THEN 60 ELSE request_timeout_seconds END
    WHERE config_id = @config_id;
END;

COMMIT TRANSACTION;
