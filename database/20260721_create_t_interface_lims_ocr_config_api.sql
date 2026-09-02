IF OBJECT_ID(N'dbo.t_interface_lims_ocr_config_api', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.t_interface_lims_ocr_config_api
    (
        config_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT pk_t_interface_lims_ocr_config_api PRIMARY KEY CLUSTERED
            CONSTRAINT df_t_interface_lims_ocr_config_api_config_id DEFAULT NEWSEQUENTIALID(),
        is_enabled BIT NOT NULL CONSTRAINT df_t_interface_lims_ocr_config_api_is_enabled DEFAULT(1),
        input_ocr_url NVARCHAR(500) NOT NULL,
        input_ocr_file_url NVARCHAR(500) NULL,
        submission_mode NVARCHAR(20) NOT NULL
            CONSTRAINT df_t_interface_lims_ocr_config_api_submission_mode DEFAULT(N'path'),
        input_ocr_file_field_name NVARCHAR(100) NOT NULL
            CONSTRAINT df_t_interface_lims_ocr_config_api_file_field DEFAULT(N'files'),
        input_ocr_bearer_token NVARCHAR(2000) NULL,
        update_master_url NVARCHAR(500) NULL,
        get_result_ocr_url NVARCHAR(500) NULL,
        feedback_url NVARCHAR(500) NULL,
        callback_url NVARCHAR(500) NULL,
        inbound_directory NVARCHAR(500) NOT NULL,
        processing_directory NVARCHAR(500) NOT NULL,
        success_directory NVARCHAR(500) NOT NULL,
        error_directory NVARCHAR(500) NOT NULL,
        document_host_directory NVARCHAR(500) NULL,
        document_web_path NVARCHAR(500) NULL,
        document_group NVARCHAR(64) NULL,
        flow_id NVARCHAR(100) NULL,
        interval_seconds INT NOT NULL CONSTRAINT df_t_interface_lims_ocr_config_api_interval_seconds DEFAULT(30),
        file_stable_seconds INT NOT NULL CONSTRAINT df_t_interface_lims_ocr_config_api_file_stable_seconds DEFAULT(5),
        max_send_attempts INT NOT NULL CONSTRAINT df_t_interface_lims_ocr_config_api_max_send_attempts DEFAULT(3),
        retry_delay_seconds INT NOT NULL CONSTRAINT df_t_interface_lims_ocr_config_api_retry_delay_seconds DEFAULT(5),
        request_timeout_seconds INT NOT NULL CONSTRAINT df_t_interface_lims_ocr_config_api_request_timeout_seconds DEFAULT(60),
        is_interface BIT NOT NULL CONSTRAINT df_t_interface_lims_ocr_config_api_is_interface DEFAULT(0),
        create_by NVARCHAR(25) NULL,
        create_date DATETIME NOT NULL CONSTRAINT df_t_interface_lims_ocr_config_api_create_date DEFAULT GETDATE(),
        CONSTRAINT ck_t_interface_lims_ocr_config_api_submission_mode
            CHECK (submission_mode IN (N'path', N'file'))
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.t_interface_lims_ocr_config_api)
BEGIN
    INSERT INTO dbo.t_interface_lims_ocr_config_api
        (input_ocr_url, input_ocr_file_url, submission_mode, input_ocr_file_field_name, input_ocr_bearer_token,
         update_master_url, get_result_ocr_url, feedback_url,
         callback_url, inbound_directory, processing_directory, success_directory, error_directory,
         document_host_directory, document_web_path, document_group,
         flow_id, interval_seconds, file_stable_seconds, max_send_attempts, retry_delay_seconds, request_timeout_seconds,
         is_enabled, is_interface, create_by)
    VALUES
        (N'http://localhost:5117/input_ocr', N'http://dev-hippo.ztrus.net:6206/aji/input_ocr', N'file', N'files', NULL,
         N'http://dev-hippo.ztrus.net:6206/aji/update_master_data',
         N'http://dev-hippo.ztrus.net:6206/aji/get_result_ocr',
         N'http://dev-hippo.ztrus.net:6206/aji/feedback',
         NULL, N'1_Inbound', N'2_Processing', N'3_Success', N'4_Error',
         NULL, N'../_Documents', N'QC_COA',
         N'6a5efa97abaf97614454562a', 30, 5, 3, 5, 60, 1, 0, N'system');
END
GO
