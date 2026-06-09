USE [LIMS_NK];
GO

/* 1) Drop FK constraints before renaming columns */
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'fk_ocr_item_result_id')
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_result_item DROP CONSTRAINT fk_ocr_item_result_id;
END
GO

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'fk_ocr_result_job_id')
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_result DROP CONSTRAINT fk_ocr_result_job_id;
END
GO

/* 2) Drop unique job_task_id to allow storing every callback call */
IF EXISTS (
    SELECT 1
    FROM sys.key_constraints
    WHERE name = 'uq_ocr_callback_job_task_id'
      AND parent_object_id = OBJECT_ID('dbo.t_interface_lims_ocr_callback')
)
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_callback
    DROP CONSTRAINT uq_ocr_callback_job_task_id;
END
GO

/* 3) Rename key/foreign key columns to explicit callback_id/result_id/item_id */
IF COL_LENGTH('dbo.t_interface_lims_ocr_callback', 'id') IS NOT NULL
   AND COL_LENGTH('dbo.t_interface_lims_ocr_callback', 'callback_id') IS NULL
BEGIN
    EXEC sp_rename 'dbo.t_interface_lims_ocr_callback.id', 'callback_id', 'COLUMN';
END
GO

IF COL_LENGTH('dbo.t_interface_lims_ocr_result', 'id') IS NOT NULL
   AND COL_LENGTH('dbo.t_interface_lims_ocr_result', 'result_id') IS NULL
BEGIN
    EXEC sp_rename 'dbo.t_interface_lims_ocr_result.id', 'result_id', 'COLUMN';
END
GO

IF COL_LENGTH('dbo.t_interface_lims_ocr_result', 'job_id') IS NOT NULL
   AND COL_LENGTH('dbo.t_interface_lims_ocr_result', 'callback_id') IS NULL
BEGIN
    EXEC sp_rename 'dbo.t_interface_lims_ocr_result.job_id', 'callback_id', 'COLUMN';
END
GO

IF COL_LENGTH('dbo.t_interface_lims_ocr_result_item', 'id') IS NOT NULL
   AND COL_LENGTH('dbo.t_interface_lims_ocr_result_item', 'item_id') IS NULL
BEGIN
    EXEC sp_rename 'dbo.t_interface_lims_ocr_result_item.id', 'item_id', 'COLUMN';
END
GO

/* 4) Recreate FK constraints with new column names */
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'fk_ocr_result_callback_id')
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_result
    ADD CONSTRAINT fk_ocr_result_callback_id
        FOREIGN KEY (callback_id)
        REFERENCES dbo.t_interface_lims_ocr_callback (callback_id)
        ON DELETE CASCADE;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'fk_ocr_item_result_id')
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_result_item
    ADD CONSTRAINT fk_ocr_item_result_id
        FOREIGN KEY (result_id)
        REFERENCES dbo.t_interface_lims_ocr_result (result_id)
        ON DELETE CASCADE;
END
GO

/* 5) Rebuild indexes for current key names */
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_ocr_result_job_id' AND object_id = OBJECT_ID('dbo.t_interface_lims_ocr_result'))
BEGIN
    DROP INDEX idx_ocr_result_job_id ON dbo.t_interface_lims_ocr_result;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_ocr_result_callback_id' AND object_id = OBJECT_ID('dbo.t_interface_lims_ocr_result'))
BEGIN
    CREATE INDEX idx_ocr_result_callback_id
        ON dbo.t_interface_lims_ocr_result (callback_id);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_ocr_callback_job_task_id' AND object_id = OBJECT_ID('dbo.t_interface_lims_ocr_callback'))
BEGIN
    CREATE INDEX idx_ocr_callback_job_task_id
        ON dbo.t_interface_lims_ocr_callback (job_task_id);
END
GO

/* 6) Ensure per-callback page uniqueness still applies */
IF EXISTS (
    SELECT 1
    FROM sys.key_constraints
    WHERE name = 'uq_ocr_result_job_page'
      AND parent_object_id = OBJECT_ID('dbo.t_interface_lims_ocr_result')
)
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_result
    DROP CONSTRAINT uq_ocr_result_job_page;
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.key_constraints
    WHERE name = 'uq_ocr_result_callback_page'
      AND parent_object_id = OBJECT_ID('dbo.t_interface_lims_ocr_result')
)
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_result
    ADD CONSTRAINT uq_ocr_result_callback_page UNIQUE (callback_id, page_id);
END
GO

/* 7) Add is_interface bit default false on all interface tables */
IF COL_LENGTH('dbo.t_interface_lims_ocr_callback', 'is_interface') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_callback
    ADD is_interface BIT NOT NULL CONSTRAINT df_ocr_callback_is_interface DEFAULT (0);
END
GO

IF COL_LENGTH('dbo.t_interface_lims_ocr_result', 'is_interface') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_result
    ADD is_interface BIT NOT NULL CONSTRAINT df_ocr_result_is_interface DEFAULT (0);
END
GO

IF COL_LENGTH('dbo.t_interface_lims_ocr_result_item', 'is_interface') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_result_item
    ADD is_interface BIT NOT NULL CONSTRAINT df_ocr_item_is_interface DEFAULT (0);
END
GO
