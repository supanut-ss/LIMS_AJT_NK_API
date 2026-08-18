IF OBJECT_ID(N'dbo.t_interface_lims_ocr_log', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.t_interface_lims_ocr_log
    (
        log_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT pk_t_interface_lims_ocr_log PRIMARY KEY CLUSTERED
            CONSTRAINT df_t_interface_lims_ocr_log_log_id DEFAULT NEWSEQUENTIALID(),
        api_name NVARCHAR(100) NOT NULL,
        request_url NVARCHAR(500) NULL,
        flow_id NVARCHAR(100) NULL,
        job_task_id NVARCHAR(100) NULL,
        file_path NVARCHAR(500) NULL,
        request_payload NVARCHAR(MAX) NULL,
        response_status_code INT NULL,
        response_payload NVARCHAR(MAX) NULL,
        is_success BIT NOT NULL CONSTRAINT df_t_interface_lims_ocr_log_is_success DEFAULT(0),
        error_message NVARCHAR(MAX) NULL,
        source_system NVARCHAR(50) NULL,
        work_status NVARCHAR(50) NOT NULL CONSTRAINT df_t_interface_lims_ocr_log_work_status DEFAULT(N'submitted'),
        attempt_count INT NOT NULL CONSTRAINT df_t_interface_lims_ocr_log_attempt_count DEFAULT(0),
        final_path NVARCHAR(500) NULL,
        completed_date DATETIME NULL,
        is_interface BIT NOT NULL CONSTRAINT df_t_interface_lims_ocr_log_is_interface DEFAULT(0),
        create_by NVARCHAR(25) NULL,
        create_date DATETIME NOT NULL CONSTRAINT df_t_interface_lims_ocr_log_create_date DEFAULT GETDATE()
    );

    CREATE INDEX ix_t_interface_lims_ocr_log_create_date ON dbo.t_interface_lims_ocr_log(create_date DESC);
    CREATE INDEX ix_t_interface_lims_ocr_log_job_task_id ON dbo.t_interface_lims_ocr_log(job_task_id);
    CREATE INDEX ix_t_interface_lims_ocr_log_flow_id ON dbo.t_interface_lims_ocr_log(flow_id);
END
GO
