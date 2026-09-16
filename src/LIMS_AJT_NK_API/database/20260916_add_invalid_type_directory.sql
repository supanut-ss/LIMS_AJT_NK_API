SET XACT_ABORT ON;

BEGIN TRANSACTION;

IF COL_LENGTH('dbo.t_interface_lims_ocr_config_api', 'invalid_type_directory') IS NULL
    ALTER TABLE dbo.t_interface_lims_ocr_config_api
        ADD invalid_type_directory NVARCHAR(500) NOT NULL
            CONSTRAINT DF_lims_ocr_config_invalid_type_directory DEFAULT (N'5_InvalidType') WITH VALUES;

COMMIT TRANSACTION;
