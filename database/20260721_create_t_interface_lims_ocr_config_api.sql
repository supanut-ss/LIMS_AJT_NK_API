IF OBJECT_ID(N'dbo.t_interface_lims_ocr_config_api', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.t_interface_lims_ocr_config_api
    (
        config_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT pk_t_interface_lims_ocr_config_api PRIMARY KEY CLUSTERED
            CONSTRAINT df_t_interface_lims_ocr_config_api_config_id DEFAULT NEWSEQUENTIALID(),
        is_enabled BIT NOT NULL CONSTRAINT df_t_interface_lims_ocr_config_api_is_enabled DEFAULT(1),
        input_ocr_url NVARCHAR(500) NOT NULL,
        callback_url NVARCHAR(500) NULL,
        inbound_directory NVARCHAR(500) NOT NULL,
        processing_directory NVARCHAR(500) NOT NULL,
        success_directory NVARCHAR(500) NOT NULL,
        error_directory NVARCHAR(500) NOT NULL,
        flow_id NVARCHAR(100) NULL,
        interval_seconds INT NOT NULL CONSTRAINT df_t_interface_lims_ocr_config_api_interval_seconds DEFAULT(30),
        is_interface BIT NOT NULL CONSTRAINT df_t_interface_lims_ocr_config_api_is_interface DEFAULT(0),
        create_by NVARCHAR(25) NULL,
        create_date DATETIME NOT NULL CONSTRAINT df_t_interface_lims_ocr_config_api_create_date DEFAULT GETDATE()
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.t_interface_lims_ocr_config_api)
BEGIN
    INSERT INTO dbo.t_interface_lims_ocr_config_api
        (input_ocr_url, callback_url, inbound_directory, processing_directory, success_directory, error_directory, flow_id, interval_seconds, is_enabled, is_interface, create_by)
    VALUES
        (N'http://localhost:5117/input_ocr', N'http://localhost:5117/api/call_back', N'1_Inbound', N'2_Processing', N'3_Success', N'4_Error', NULL, 30, 1, 0, N'system');
END
GO
