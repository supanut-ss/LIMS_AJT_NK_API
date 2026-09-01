SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

USE [LIMS_NK];
GO

-- Existing callback rows remain NULL and are intentionally outside the unique
-- index. New API callbacks populate all four columns.
IF COL_LENGTH('dbo.t_interface_lims_ocr_callback', 'idempotency_key') IS NULL
BEGIN
    ALTER TABLE [dbo].[t_interface_lims_ocr_callback]
        ADD [idempotency_key] NVARCHAR(100) COLLATE Latin1_General_100_BIN2 NULL;
END
GO

IF COL_LENGTH('dbo.t_interface_lims_ocr_callback', 'request_hash') IS NULL
BEGIN
    ALTER TABLE [dbo].[t_interface_lims_ocr_callback]
        ADD [request_hash] CHAR(64) NULL;
END
GO

IF COL_LENGTH('dbo.t_interface_lims_ocr_callback', 'interface_status') IS NULL
BEGIN
    ALTER TABLE [dbo].[t_interface_lims_ocr_callback]
        ADD [interface_status] VARCHAR(25) NULL;
END
GO

IF COL_LENGTH('dbo.t_interface_lims_ocr_callback', 'interface_summary_json') IS NULL
BEGIN
    ALTER TABLE [dbo].[t_interface_lims_ocr_callback]
        ADD [interface_summary_json] NVARCHAR(MAX) NULL;
END
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE [name] = 'ck_ocr_callback_idempotency_pair'
      AND [parent_object_id] = OBJECT_ID('dbo.t_interface_lims_ocr_callback')
)
BEGIN
    ALTER TABLE [dbo].[t_interface_lims_ocr_callback] WITH CHECK
        ADD CONSTRAINT [ck_ocr_callback_idempotency_pair]
        CHECK (([idempotency_key] IS NULL AND [request_hash] IS NULL)
            OR ([idempotency_key] IS NOT NULL AND [request_hash] IS NOT NULL));
END
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE [name] = 'uq_ocr_callback_idempotency_key'
      AND [object_id] = OBJECT_ID('dbo.t_interface_lims_ocr_callback')
)
BEGIN
    CREATE UNIQUE INDEX [uq_ocr_callback_idempotency_key]
        ON [dbo].[t_interface_lims_ocr_callback] ([idempotency_key])
        WHERE [idempotency_key] IS NOT NULL;
END
GO
