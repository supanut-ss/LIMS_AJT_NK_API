USE [LIMS_NK];
GO

/*
    Adds the columns required by InterfaceLimsOcrLogEntity when the target
    database still has the older t_interface_lims_ocr_log schema.
*/

IF OBJECT_ID(N'dbo.t_interface_lims_ocr_log', N'U') IS NOT NULL AND COL_LENGTH('dbo.t_interface_lims_ocr_log', 'file_path') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_log
    ADD file_path NVARCHAR(500) NULL;
END
GO

IF OBJECT_ID(N'dbo.t_interface_lims_ocr_log', N'U') IS NOT NULL AND COL_LENGTH('dbo.t_interface_lims_ocr_log', 'attempt_count') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_log
    ADD attempt_count INT NOT NULL CONSTRAINT df_t_interface_lims_ocr_log_attempt_count DEFAULT (0);
END
GO

IF OBJECT_ID(N'dbo.t_interface_lims_ocr_log', N'U') IS NOT NULL AND COL_LENGTH('dbo.t_interface_lims_ocr_log', 'final_path') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_log
    ADD final_path NVARCHAR(500) NULL;
END
GO

IF OBJECT_ID(N'dbo.t_interface_lims_ocr_log', N'U') IS NOT NULL AND COL_LENGTH('dbo.t_interface_lims_ocr_log', 'completed_date') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_log
    ADD completed_date DATETIME NULL;
END
GO

IF OBJECT_ID(N'dbo.t_interface_lims_ocr_log', N'U') IS NOT NULL AND COL_LENGTH('dbo.t_interface_lims_ocr_log', 'work_status') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_log
    ADD work_status NVARCHAR(50) NOT NULL CONSTRAINT df_t_interface_lims_ocr_log_work_status DEFAULT ('submitted');
END
GO