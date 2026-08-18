SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH('dbo.t_interface_lims_ocr_result', 'file_id') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_result ADD file_id NVARCHAR(100) NULL;
END;

IF COL_LENGTH('dbo.t_interface_lims_ocr_result', 'quantity_uom') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_result ADD quantity_uom NVARCHAR(25) NULL;
END;

IF COL_LENGTH('dbo.t_interface_lims_ocr_result', 'confident_json') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_result ADD confident_json NVARCHAR(MAX) NULL;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID('dbo.t_interface_lims_ocr_result')
      AND name = 'idx_ocr_result_file_id'
)
BEGIN
    CREATE INDEX idx_ocr_result_file_id
        ON dbo.t_interface_lims_ocr_result(file_id);
END;

IF COL_LENGTH('dbo.t_interface_lims_ocr_log', 'work_status') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_log
        ADD work_status NVARCHAR(50) NOT NULL
            CONSTRAINT df_t_interface_lims_ocr_log_work_status DEFAULT(N'submitted');
END;

IF COL_LENGTH('dbo.t_interface_lims_ocr_log', 'final_path') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_log ADD final_path NVARCHAR(500) NULL;
END;

IF COL_LENGTH('dbo.t_interface_lims_ocr_log', 'attempt_count') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_log
        ADD attempt_count INT NOT NULL
            CONSTRAINT df_t_interface_lims_ocr_log_attempt_count DEFAULT(0);
END;

IF COL_LENGTH('dbo.t_interface_lims_ocr_log', 'completed_date') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_log ADD completed_date DATETIME NULL;
END;

IF COL_LENGTH('dbo.t_interface_lims_ocr_config_api', 'document_host_directory') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_config_api ADD document_host_directory NVARCHAR(500) NULL;
END;

IF COL_LENGTH('dbo.t_interface_lims_ocr_config_api', 'document_web_path') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_config_api ADD document_web_path NVARCHAR(500) NULL;
END;

IF COL_LENGTH('dbo.t_interface_lims_ocr_config_api', 'document_group') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_config_api ADD document_group NVARCHAR(64) NULL;
END;

IF COL_LENGTH('dbo.t_interface_lims_ocr_config_api', 'file_stable_seconds') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_config_api
        ADD file_stable_seconds INT NOT NULL
            CONSTRAINT df_t_interface_lims_ocr_config_api_file_stable_seconds DEFAULT(5);
END;

IF COL_LENGTH('dbo.t_interface_lims_ocr_config_api', 'max_send_attempts') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_config_api
        ADD max_send_attempts INT NOT NULL
            CONSTRAINT df_t_interface_lims_ocr_config_api_max_send_attempts DEFAULT(3);
END;

IF COL_LENGTH('dbo.t_interface_lims_ocr_config_api', 'retry_delay_seconds') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_config_api
        ADD retry_delay_seconds INT NOT NULL
            CONSTRAINT df_t_interface_lims_ocr_config_api_retry_delay_seconds DEFAULT(5);
END;

IF COL_LENGTH('dbo.t_interface_lims_ocr_config_api', 'request_timeout_seconds') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_config_api
        ADD request_timeout_seconds INT NOT NULL
            CONSTRAINT df_t_interface_lims_ocr_config_api_request_timeout_seconds DEFAULT(60);
END;

EXEC sys.sp_executesql N'
    UPDATE dbo.t_interface_lims_ocr_config_api
    SET document_web_path = COALESCE(NULLIF(LTRIM(RTRIM(document_web_path)), N''''), N''../_Documents''),
        document_group = COALESCE(NULLIF(LTRIM(RTRIM(document_group)), N''''), N''QC_COA'');';

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID('dbo.t_interface_lims_ocr_log')
      AND name = 'ix_t_interface_lims_ocr_log_create_date'
)
BEGIN
    CREATE INDEX ix_t_interface_lims_ocr_log_create_date
        ON dbo.t_interface_lims_ocr_log(create_date DESC);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID('dbo.t_interface_lims_ocr_log')
      AND name = 'ix_t_interface_lims_ocr_log_job_task_id'
)
BEGIN
    CREATE INDEX ix_t_interface_lims_ocr_log_job_task_id
        ON dbo.t_interface_lims_ocr_log(job_task_id);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID('dbo.t_interface_lims_ocr_log')
      AND name = 'ix_t_interface_lims_ocr_log_flow_id'
)
BEGIN
    CREATE INDEX ix_t_interface_lims_ocr_log_flow_id
        ON dbo.t_interface_lims_ocr_log(flow_id);
END;

COMMIT TRANSACTION;
