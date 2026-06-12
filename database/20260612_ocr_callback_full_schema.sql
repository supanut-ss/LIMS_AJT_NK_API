USE [LIMS_NK];
GO

-- ============================================================================
-- SQL Server DDL Script for OCR Callback Interface Tables (Full Initialization)
-- Target Database: LIMS_NK
-- Tables Created:
--   1. dbo.t_interface_lims_ocr_callback
--   2. dbo.t_interface_lims_ocr_result
--   3. dbo.t_interface_lims_ocr_result_item
-- ============================================================================

-- ----------------------------------------------------------------------------
-- 1. Table: dbo.t_interface_lims_ocr_callback
-- ----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[t_interface_lims_ocr_callback]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[t_interface_lims_ocr_callback] (
        [callback_id]  UNIQUEIDENTIFIER NOT NULL CONSTRAINT [df_ocr_callback_id] DEFAULT (NEWSEQUENTIALID()),
        [job_task_id]  VARCHAR(100)     NOT NULL,
        [is_interface] BIT              NOT NULL CONSTRAINT [df_ocr_callback_is_interface] DEFAULT (0),
        [create_by]    VARCHAR(25)      NULL,
        [create_date]  DATETIME         NOT NULL CONSTRAINT [df_ocr_callback_create_date] DEFAULT (GETDATE()),
        CONSTRAINT [pk_ocr_callback_id] PRIMARY KEY CLUSTERED ([callback_id] ASC)
    );

    CREATE INDEX [idx_ocr_callback_job_task_id] 
        ON [dbo].[t_interface_lims_ocr_callback] ([job_task_id] ASC);
END
GO

-- ----------------------------------------------------------------------------
-- 2. Table: dbo.t_interface_lims_ocr_result
-- ----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[t_interface_lims_ocr_result]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[t_interface_lims_ocr_result] (
        [result_id]            UNIQUEIDENTIFIER NOT NULL CONSTRAINT [df_ocr_result_id] DEFAULT (NEWSEQUENTIALID()),
        [callback_id]          UNIQUEIDENTIFIER NOT NULL,
        [is_interface]         BIT              NOT NULL CONSTRAINT [df_ocr_result_is_interface] DEFAULT (0),
        [page_id]              INT              NOT NULL,
        [tracking_id]          VARCHAR(100)     NULL,
        [tracking_status]      VARCHAR(50)      NOT NULL,
        [product_name]         VARCHAR(255)     NULL,
        [document_type]        VARCHAR(100)     NULL,
        [supplier_name]        VARCHAR(255)     NULL,
        [lot_number]           VARCHAR(100)     NULL,
        [origin_supplier_name] VARCHAR(255)     NULL,
        [origin_product_name]  VARCHAR(255)     NULL,
        [expiry_date]          DATE             NULL,
        [mfg_date]             DATE             NULL,
        [internal_lot]         VARCHAR(50)      NULL,
        [quantity]             DECIMAL(18, 4)   NULL,
        [create_by]            VARCHAR(25)      NULL,
        [create_date]          DATETIME         NOT NULL CONSTRAINT [df_ocr_result_create_date] DEFAULT (GETDATE()),
        CONSTRAINT [pk_ocr_result_id] PRIMARY KEY CLUSTERED ([result_id] ASC),
        CONSTRAINT [uq_ocr_result_callback_page] UNIQUE ([callback_id], [page_id]),
        CONSTRAINT [fk_ocr_result_callback_id] FOREIGN KEY ([callback_id]) 
            REFERENCES [dbo].[t_interface_lims_ocr_callback] ([callback_id]) ON DELETE CASCADE
    );

    CREATE INDEX [idx_ocr_result_callback_id] 
        ON [dbo].[t_interface_lims_ocr_result] ([callback_id] ASC);
        
    CREATE INDEX [idx_ocr_result_tracking_id] 
        ON [dbo].[t_interface_lims_ocr_result] ([tracking_id] ASC);
        
    CREATE INDEX [idx_ocr_result_tracking_status] 
        ON [dbo].[t_interface_lims_ocr_result] ([tracking_status] ASC);
        
    CREATE INDEX [idx_ocr_result_lot_number] 
        ON [dbo].[t_interface_lims_ocr_result] ([lot_number] ASC);
        
    CREATE INDEX [idx_ocr_result_expiry_date] 
        ON [dbo].[t_interface_lims_ocr_result] ([expiry_date] ASC);
END
GO

-- ----------------------------------------------------------------------------
-- 3. Table: dbo.t_interface_lims_ocr_result_item
-- ----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[t_interface_lims_ocr_result_item]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[t_interface_lims_ocr_result_item] (
        [item_id]        UNIQUEIDENTIFIER NOT NULL CONSTRAINT [df_ocr_item_id] DEFAULT (NEWSEQUENTIALID()),
        [result_id]      UNIQUEIDENTIFIER NOT NULL,
        [is_interface]   BIT              NOT NULL CONSTRAINT [df_ocr_item_is_interface] DEFAULT (0),
        [seq]            INT              NOT NULL,
        [parameter_name] VARCHAR(255)     NOT NULL,
        [result]         VARCHAR(255)     NULL,
        [uom]            VARCHAR(25)      NULL,
        [create_by]      VARCHAR(25)      NULL,
        [create_date]    DATETIME         NOT NULL CONSTRAINT [df_ocr_item_create_date] DEFAULT (GETDATE()),
        CONSTRAINT [pk_ocr_result_item_id] PRIMARY KEY CLUSTERED ([item_id] ASC),
        CONSTRAINT [uq_ocr_result_item_result_seq] UNIQUE ([result_id], [seq]),
        CONSTRAINT [fk_ocr_item_result_id] FOREIGN KEY ([result_id]) 
            REFERENCES [dbo].[t_interface_lims_ocr_result] ([result_id]) ON DELETE CASCADE
    );

    CREATE INDEX [idx_ocr_result_item_result_id] 
        ON [dbo].[t_interface_lims_ocr_result_item] ([result_id] ASC);
        
    CREATE INDEX [idx_ocr_result_item_parameter_name] 
        ON [dbo].[t_interface_lims_ocr_result_item] ([parameter_name] ASC);
END
GO
