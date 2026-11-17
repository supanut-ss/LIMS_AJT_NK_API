SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

USE [LIMS_NK];
GO

-- Ports the matching/interface logic from OcrCallbackInterfaceService (C#) into a
-- stored procedure so WMS_NEW (Web) and LIMS_AJT_NK_CallbackWorker (Worker) can
-- remap an OCR callback directly against the shared DB, without an HTTP call
-- into LIMS_AJT_NK_API. The API's POST /api/call_back still calls this same
-- procedure, then (only there) performs filesystem document storage, which SQL
-- Server cannot do.
--
-- Behavior mirrors OcrCallbackInterfaceService.ProcessAsync 1:1, except document
-- fields (document_status/document_id/document_path) are never populated here;
-- the API fills those in and rewrites interface_summary_json after this proc
-- returns, exactly as it did when the matching ran in C#.
CREATE OR ALTER PROCEDURE [dbo].[usp_ocr_callback_remap]
    @callback_id UNIQUEIDENTIFIER,
    @force_reprocess BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @started_tran BIT = 0;
    IF @@TRANCOUNT = 0
    BEGIN
        BEGIN TRANSACTION;
        SET @started_tran = 1;
    END

    BEGIN TRY
        DECLARE @existing_status VARCHAR(25);
        DECLARE @existing_summary NVARCHAR(MAX);

        SELECT
            @existing_status = [interface_status],
            @existing_summary = [interface_summary_json]
        FROM [dbo].[t_interface_lims_ocr_callback] WITH (UPDLOCK, HOLDLOCK)
        WHERE [callback_id] = @callback_id;

        IF @@ROWCOUNT = 0
        BEGIN
            RAISERROR('OCR callback was not found.', 16, 1);
        END

        -- Replay: same short-circuit as IsTerminal(...) && summary present in C#.
        IF @force_reprocess = 0
            AND @existing_status IN ('completed', 'partial', 'not_matched')
            AND @existing_summary IS NOT NULL
            AND LEN(@existing_summary) > 0
        BEGIN
            IF @started_tran = 1 COMMIT TRANSACTION;

            SELECT @existing_summary AS summary_json, CAST(1 AS BIT) AS is_replay;
            RETURN;
        END

        DECLARE @pages TABLE
        (
            page_id INT PRIMARY KEY,
            result_id UNIQUEIDENTIFIER NOT NULL,
            receipt_detail_id UNIQUEIDENTIFIER NULL,
            status VARCHAR(20) NOT NULL,
            reason VARCHAR(50) NULL,
            updated_item_count INT NOT NULL DEFAULT 0,
            skipped_item_count INT NOT NULL DEFAULT 0,
            already_processed_item_count INT NOT NULL DEFAULT 0
        );

        DECLARE @items TABLE
        (
            page_id INT NOT NULL,
            seq INT NOT NULL,
            parameter_name NVARCHAR(255) NOT NULL,
            status VARCHAR(20) NOT NULL,
            reason VARCHAR(50) NULL
        );

        DECLARE @has_eligible BIT = 0;
        IF EXISTS
        (
            SELECT 1
            FROM [dbo].[t_interface_lims_ocr_result]
            WHERE [callback_id] = @callback_id
              AND LTRIM(RTRIM([tracking_status])) = 'ReadyToCheck'
        )
            SET @has_eligible = 1;

        -- Pages that are not ReadyToCheck are ignored, same as IsReadyToCheck(...) in C#.
        INSERT INTO @pages (page_id, result_id, status, reason)
        SELECT [page_id], [result_id], 'ignored', 'tracking_status_not_ready'
        FROM [dbo].[t_interface_lims_ocr_result]
        WHERE [callback_id] = @callback_id
          AND LTRIM(RTRIM([tracking_status])) <> 'ReadyToCheck';

        DECLARE @result_id UNIQUEIDENTIFIER;
        DECLARE @page_id INT;
        DECLARE @product_name NVARCHAR(255);
        DECLARE @supplier_name NVARCHAR(255);
        DECLARE @internal_lot NVARCHAR(50);
        DECLARE @mfg_date DATE;

        DECLARE result_cursor CURSOR LOCAL FAST_FORWARD FOR
            SELECT [result_id], [page_id], [product_name], [supplier_name], [internal_lot], [mfg_date]
            FROM [dbo].[t_interface_lims_ocr_result]
            WHERE [callback_id] = @callback_id
              AND LTRIM(RTRIM([tracking_status])) = 'ReadyToCheck'
            ORDER BY [page_id];

        OPEN result_cursor;
        FETCH NEXT FROM result_cursor INTO @result_id, @page_id, @product_name, @supplier_name, @internal_lot, @mfg_date;

        WHILE @@FETCH_STATUS = 0
        BEGIN
            DECLARE @match_error VARCHAR(50) = NULL;
            DECLARE @receipt_detail_id UNIQUEIDENTIFIER = NULL;
            DECLARE @page_fail_reason VARCHAR(50) = NULL;

            IF @product_name IS NULL OR LTRIM(RTRIM(@product_name)) = ''
                SET @match_error = 'product_name_missing';
            ELSE IF @supplier_name IS NULL OR LTRIM(RTRIM(@supplier_name)) = ''
                SET @match_error = 'supplier_name_missing';
            ELSE IF @internal_lot IS NULL OR LTRIM(RTRIM(@internal_lot)) = ''
                SET @match_error = 'internal_lot_missing';
            ELSE IF @mfg_date IS NULL
                SET @match_error = 'mfg_date_missing';

            IF @match_error IS NOT NULL
            BEGIN
                INSERT INTO @pages (page_id, result_id, status, reason)
                VALUES (@page_id, @result_id, 'not_matched', @match_error);

                INSERT INTO @items (page_id, seq, parameter_name, status, reason)
                SELECT @page_id, [seq], [parameter_name], 'skipped', @match_error
                FROM [dbo].[t_interface_lims_ocr_result_item]
                WHERE [result_id] = @result_id;

                UPDATE @pages
                SET skipped_item_count = (SELECT COUNT(*) FROM @items WHERE page_id = @page_id)
                WHERE page_id = @page_id;

                UPDATE [dbo].[t_interface_lims_ocr_result]
                SET [is_interface] = 0
                WHERE [result_id] = @result_id;
            END
            ELSE
            BEGIN
                DECLARE @trimmed_product NVARCHAR(255) = LTRIM(RTRIM(@product_name));
                DECLARE @trimmed_supplier NVARCHAR(255) = LTRIM(RTRIM(@supplier_name));
                DECLARE @trimmed_lot NVARCHAR(50) = LTRIM(RTRIM(@internal_lot));
                DECLARE @inbound_count INT;

                SELECT @inbound_count = COUNT(*)
                FROM
                (
                    SELECT TOP (2) [receipt_detail_id]
                    FROM [dbo].[t_lims_inbound_receive]
                    WHERE [item_rm_name] IS NOT NULL
                      AND [supplier_name] IS NOT NULL
                      AND [lot_number] IS NOT NULL
                      AND LTRIM(RTRIM([item_rm_name])) = @trimmed_product
                      AND LTRIM(RTRIM([supplier_name])) = @trimmed_supplier
                      AND LTRIM(RTRIM([lot_number])) = @trimmed_lot
                      AND [mfg_date] = @mfg_date
                ) AS candidates;

                IF @inbound_count = 0
                    SET @page_fail_reason = 'inbound_not_found';
                ELSE IF @inbound_count > 1
                    SET @page_fail_reason = 'inbound_ambiguous';
                ELSE
                BEGIN
                    SELECT TOP (1) @receipt_detail_id = [receipt_detail_id]
                    FROM [dbo].[t_lims_inbound_receive]
                    WHERE [item_rm_name] IS NOT NULL
                      AND [supplier_name] IS NOT NULL
                      AND [lot_number] IS NOT NULL
                      AND LTRIM(RTRIM([item_rm_name])) = @trimmed_product
                      AND LTRIM(RTRIM([supplier_name])) = @trimmed_supplier
                      AND LTRIM(RTRIM([lot_number])) = @trimmed_lot
                      AND [mfg_date] = @mfg_date;

                    DECLARE @scan_count INT;
                    SELECT @scan_count = COUNT(*)
                    FROM
                    (
                        SELECT TOP (2) [qaqc_scan_id]
                        FROM [dbo].[t_lims_qaqc_scan]
                        WHERE [receipt_detail_id] = @receipt_detail_id
                    ) AS scans;

                    -- Mirror QAQCPostFileToServer.ashx.cs: when a COA document is
                    -- attached for a receipt with no QAQC scan yet, it creates one
                    -- from the inbound receive + the active t_wms_item_rm row for
                    -- that item/supplier. Remapping should be able to do the same,
                    -- instead of failing with qaqc_scan_not_found, so a callback
                    -- that arrives before any manual attachment can still match.
                    IF @scan_count = 0
                    BEGIN
                        DECLARE @rm_found BIT = 0;
                        DECLARE @new_scan_item_rm_code VARCHAR(50) = NULL;
                        DECLARE @new_scan_item_rm_name VARCHAR(50) = NULL;
                        DECLARE @inbound_item_master_id UNIQUEIDENTIFIER;
                        DECLARE @inbound_supplier_id UNIQUEIDENTIFIER;
                        DECLARE @inbound_item_number VARCHAR(50);
                        DECLARE @inbound_lot_number VARCHAR(50);
                        DECLARE @inbound_expiry_date VARCHAR(8);
                        DECLARE @inbound_scan_mfg_date DATE;

                        SELECT
                            @inbound_item_master_id = [item_master_id],
                            @inbound_supplier_id = [supplier_id],
                            @inbound_item_number = [item_number],
                            @inbound_lot_number = [lot_number],
                            @inbound_expiry_date = [expiry_date],
                            @inbound_scan_mfg_date = [mfg_date]
                        FROM [dbo].[t_lims_inbound_receive]
                        WHERE [receipt_detail_id] = @receipt_detail_id;

                        SELECT TOP (1)
                            @new_scan_item_rm_code = [item_rm_code],
                            @new_scan_item_rm_name = [item_rm_name],
                            @rm_found = 1
                        FROM [dbo].[t_wms_item_rm]
                        WHERE [item_master_id] = @inbound_item_master_id
                          AND [supplier_id] = @inbound_supplier_id
                          AND [is_active] = 'YES';

                        IF @rm_found = 1
                        BEGIN
                            INSERT INTO [dbo].[t_lims_qaqc_scan]
                                ([qaqc_scan_id], [receipt_detail_id], [item_number], [item_rm_code], [item_rm_name], [lot_number], [expiry_date], [mfg_date])
                            VALUES
                                (NEWID(), @receipt_detail_id, @inbound_item_number, @new_scan_item_rm_code, @new_scan_item_rm_name, @inbound_lot_number, @inbound_expiry_date, @inbound_scan_mfg_date);

                            SET @scan_count = 1;
                        END
                    END

                    IF @scan_count = 0
                        SET @page_fail_reason = 'qaqc_scan_not_found';
                    ELSE IF @scan_count > 1
                        SET @page_fail_reason = 'qaqc_scan_ambiguous';
                END

                IF @page_fail_reason IS NOT NULL
                BEGIN
                    INSERT INTO @pages (page_id, result_id, receipt_detail_id, status, reason)
                    VALUES (@page_id, @result_id, @receipt_detail_id, 'not_matched', @page_fail_reason);

                    INSERT INTO @items (page_id, seq, parameter_name, status, reason)
                    SELECT @page_id, [seq], [parameter_name], 'skipped', @page_fail_reason
                    FROM [dbo].[t_interface_lims_ocr_result_item]
                    WHERE [result_id] = @result_id;

                    UPDATE @pages
                    SET skipped_item_count = (SELECT COUNT(*) FROM @items WHERE page_id = @page_id)
                    WHERE page_id = @page_id;

                    UPDATE [dbo].[t_interface_lims_ocr_result]
                    SET [is_interface] = 0
                    WHERE [result_id] = @result_id;
                END
                ELSE
                BEGIN
                    DECLARE @item_total INT;
                    SELECT @item_total = COUNT(*)
                    FROM [dbo].[t_interface_lims_ocr_result_item]
                    WHERE [result_id] = @result_id;

                    IF @item_total = 0
                    BEGIN
                        INSERT INTO @pages (page_id, result_id, receipt_detail_id, status, reason)
                        VALUES (@page_id, @result_id, @receipt_detail_id, 'not_matched', 'parameter_items_missing');

                        UPDATE [dbo].[t_interface_lims_ocr_result]
                        SET [is_interface] = 0
                        WHERE [result_id] = @result_id;
                    END
                    ELSE
                    BEGIN
                        INSERT INTO @pages (page_id, result_id, receipt_detail_id, status, reason)
                        VALUES (@page_id, @result_id, @receipt_detail_id, 'pending', NULL);

                        DECLARE @item_id UNIQUEIDENTIFIER;
                        DECLARE @seq INT;
                        DECLARE @parameter_name NVARCHAR(255);
                        DECLARE @result_value NVARCHAR(255);
                        DECLARE @item_is_interface BIT;

                        DECLARE item_cursor CURSOR LOCAL FAST_FORWARD FOR
                            SELECT [item_id], [seq], [parameter_name], [result], [is_interface]
                            FROM [dbo].[t_interface_lims_ocr_result_item]
                            WHERE [result_id] = @result_id
                            ORDER BY [seq];

                        OPEN item_cursor;
                        FETCH NEXT FROM item_cursor INTO @item_id, @seq, @parameter_name, @result_value, @item_is_interface;

                        WHILE @@FETCH_STATUS = 0
                        BEGIN
                            DECLARE @item_status VARCHAR(20) = 'skipped';
                            DECLARE @item_reason VARCHAR(50) = NULL;

                            IF @item_is_interface = 1
                                OR EXISTS (SELECT 1 FROM [dbo].[t_lims_qaqc_coa_parameter_transaction] WHERE [tran_id] = @item_id)
                            BEGIN
                                SET @item_status = 'already_processed';

                                UPDATE [dbo].[t_interface_lims_ocr_result_item]
                                SET [is_interface] = 1
                                WHERE [item_id] = @item_id;
                            END
                            ELSE IF @result_value IS NULL OR LTRIM(RTRIM(@result_value)) = ''
                            BEGIN
                                SET @item_reason = 'result_missing';
                            END
                            ELSE
                            BEGIN
                                DECLARE @normalized_result NVARCHAR(255) = LTRIM(RTRIM(@result_value));

                                IF LEN(@normalized_result) > 50
                                BEGIN
                                    SET @item_reason = 'result_too_long';
                                END
                                ELSE
                                BEGIN
                                    DECLARE @normalized_param NVARCHAR(255) = LTRIM(RTRIM(@parameter_name));
                                    DECLARE @mapping_count INT;

                                    SELECT @mapping_count = COUNT(*)
                                    FROM
                                    (
                                        SELECT TOP (2) [coa_mapping_parameter_id]
                                        FROM [dbo].[t_lims_coa_parameter_mapping]
                                        WHERE LTRIM(RTRIM([is_active])) = 'YES'
                                          AND [ocr_parameter_name] IS NOT NULL
                                          AND [coa_parameter_name] IS NOT NULL
                                          AND LTRIM(RTRIM([ocr_parameter_name])) = @normalized_param
                                    ) AS mappings;

                                    IF @mapping_count = 0
                                        SET @item_reason = 'mapping_not_found';
                                    ELSE IF @mapping_count > 1
                                        SET @item_reason = 'mapping_ambiguous';
                                    ELSE
                                    BEGIN
                                        DECLARE @coa_parameter_name NVARCHAR(100);
                                        SELECT TOP (1) @coa_parameter_name = LTRIM(RTRIM([coa_parameter_name]))
                                        FROM [dbo].[t_lims_coa_parameter_mapping]
                                        WHERE LTRIM(RTRIM([is_active])) = 'YES'
                                          AND [ocr_parameter_name] IS NOT NULL
                                          AND [coa_parameter_name] IS NOT NULL
                                          AND LTRIM(RTRIM([ocr_parameter_name])) = @normalized_param;

                                        DECLARE @coa_parameter_count INT;
                                        SELECT @coa_parameter_count = COUNT(*)
                                        FROM
                                        (
                                            SELECT TOP (2) [coa_parameter_id]
                                            FROM [dbo].[t_lims_coa_parameter]
                                            WHERE LTRIM(RTRIM([is_active])) = 'YES'
                                              AND [coa_parameter_name] IS NOT NULL
                                              AND LTRIM(RTRIM([coa_parameter_name])) = @coa_parameter_name
                                        ) AS coa_parameters;

                                        IF @coa_parameter_count = 0
                                            SET @item_reason = 'coa_parameter_not_found';
                                        ELSE IF @coa_parameter_count > 1
                                            SET @item_reason = 'coa_parameter_ambiguous';
                                        ELSE
                                        BEGIN
                                            DECLARE @coa_parameter_id UNIQUEIDENTIFIER;
                                            SELECT TOP (1) @coa_parameter_id = [coa_parameter_id]
                                            FROM [dbo].[t_lims_coa_parameter]
                                            WHERE LTRIM(RTRIM([is_active])) = 'YES'
                                              AND [coa_parameter_name] IS NOT NULL
                                              AND LTRIM(RTRIM([coa_parameter_name])) = @coa_parameter_name;

                                            DECLARE @target_count INT;
                                            SELECT @target_count = COUNT(*)
                                            FROM
                                            (
                                                SELECT TOP (2) manage.[manage_coa_parameter_id]
                                                FROM [dbo].[t_lims_manage_coa_parameter] AS manage
                                                INNER JOIN [dbo].[t_lims_qaqc_coa_parameter_test] AS test
                                                    ON manage.[manage_coa_parameter_id] = test.[manage_coa_parameter_id]
                                                WHERE manage.[coa_parameter_id] = @coa_parameter_id
                                                  AND LTRIM(RTRIM(manage.[is_active])) = 'YES'
                                                  AND test.[receipt_detail_id] = @receipt_detail_id
                                            ) AS targets;

                                            IF @target_count = 0
                                                SET @item_reason = 'parameter_test_not_found';
                                            ELSE IF @target_count > 1
                                                SET @item_reason = 'parameter_test_ambiguous';
                                            ELSE
                                            BEGIN
                                                DECLARE @manage_coa_parameter_id UNIQUEIDENTIFIER;
                                                DECLARE @coa_parameter_test_id UNIQUEIDENTIFIER;
                                                DECLARE @result_between NVARCHAR(50);

                                                SELECT TOP (1)
                                                    @manage_coa_parameter_id = manage.[manage_coa_parameter_id],
                                                    @coa_parameter_test_id = test.[coa_parameter_test_id],
                                                    @result_between = test.[result_between]
                                                FROM [dbo].[t_lims_manage_coa_parameter] AS manage
                                                INNER JOIN [dbo].[t_lims_qaqc_coa_parameter_test] AS test
                                                    ON manage.[manage_coa_parameter_id] = test.[manage_coa_parameter_id]
                                                WHERE manage.[coa_parameter_id] = @coa_parameter_id
                                                  AND LTRIM(RTRIM(manage.[is_active])) = 'YES'
                                                  AND test.[receipt_detail_id] = @receipt_detail_id;

                                                DECLARE @inbound_receive_date DATE;
                                                DECLARE @inbound_uom_id UNIQUEIDENTIFIER;
                                                SELECT
                                                    @inbound_receive_date = [receive_date],
                                                    @inbound_uom_id = [uom_id]
                                                FROM [dbo].[t_lims_inbound_receive]
                                                WHERE [receipt_detail_id] = @receipt_detail_id;

                                                UPDATE [dbo].[t_lims_qaqc_coa_parameter_test]
                                                SET [result] = @normalized_result,
                                                    [update_by] = 'api',
                                                    [update_date] = GETDATE()
                                                WHERE [coa_parameter_test_id] = @coa_parameter_test_id;

                                                INSERT INTO [dbo].[t_lims_qaqc_coa_parameter_transaction]
                                                (
                                                    [tran_id], [log_type], [receipt_detail_id], [manage_coa_parameter_id],
                                                    [result], [result_between], [receive_date], [uom_id],
                                                    [transation_by], [transaction_date]
                                                )
                                                VALUES
                                                (
                                                    @item_id, 'OCR Callback', @receipt_detail_id, @manage_coa_parameter_id,
                                                    @normalized_result, @result_between, @inbound_receive_date, @inbound_uom_id,
                                                    'api', GETDATE()
                                                );

                                                UPDATE [dbo].[t_interface_lims_ocr_result_item]
                                                SET [is_interface] = 1
                                                WHERE [item_id] = @item_id;

                                                SET @item_status = 'updated';
                                            END
                                        END
                                    END
                                END
                            END

                            INSERT INTO @items (page_id, seq, parameter_name, status, reason)
                            VALUES (@page_id, @seq, @parameter_name, @item_status, @item_reason);

                            FETCH NEXT FROM item_cursor INTO @item_id, @seq, @parameter_name, @result_value, @item_is_interface;
                        END

                        CLOSE item_cursor;
                        DEALLOCATE item_cursor;

                        UPDATE @pages
                        SET updated_item_count = (SELECT COUNT(*) FROM @items WHERE page_id = @page_id AND status = 'updated'),
                            skipped_item_count = (SELECT COUNT(*) FROM @items WHERE page_id = @page_id AND status = 'skipped'),
                            already_processed_item_count = (SELECT COUNT(*) FROM @items WHERE page_id = @page_id AND status = 'already_processed')
                        WHERE page_id = @page_id;

                        DECLARE @processed_count INT;
                        DECLARE @page_skipped_count INT;
                        SELECT
                            @processed_count = updated_item_count + already_processed_item_count,
                            @page_skipped_count = skipped_item_count
                        FROM @pages
                        WHERE page_id = @page_id;

                        UPDATE @pages
                        SET status = CASE
                                WHEN @processed_count > 0 AND @page_skipped_count = 0 THEN 'completed'
                                WHEN @processed_count = 0 THEN 'not_matched'
                                ELSE 'partial'
                            END
                        WHERE page_id = @page_id;

                        UPDATE [dbo].[t_interface_lims_ocr_result]
                        SET [is_interface] = CASE WHEN @processed_count > 0 AND @page_skipped_count = 0 THEN 1 ELSE 0 END
                        WHERE [result_id] = @result_id;
                    END
                END
            END

            FETCH NEXT FROM result_cursor INTO @result_id, @page_id, @product_name, @supplier_name, @internal_lot, @mfg_date;
        END

        CLOSE result_cursor;
        DEALLOCATE result_cursor;

        DECLARE @updated_total INT, @skipped_total INT, @already_total INT;
        SELECT
            @updated_total = SUM(updated_item_count),
            @skipped_total = SUM(skipped_item_count),
            @already_total = SUM(already_processed_item_count)
        FROM @pages;

        DECLARE @all_non_ignored_completed BIT = 1;
        IF EXISTS (SELECT 1 FROM @pages WHERE status <> 'ignored' AND status <> 'completed')
            SET @all_non_ignored_completed = 0;

        DECLARE @completed BIT = CASE WHEN @has_eligible = 1 AND @all_non_ignored_completed = 1 THEN 1 ELSE 0 END;

        DECLARE @interface_status VARCHAR(25) =
            CASE
                WHEN @completed = 1 THEN 'completed'
                WHEN ISNULL(@updated_total, 0) + ISNULL(@already_total, 0) = 0 THEN 'not_matched'
                ELSE 'partial'
            END;

        -- JSON_QUERY wraps each nested FOR JSON subquery so [pages]/[items] stay
        -- real nested arrays instead of escaped strings, matching the C#
        -- OcrCallbackInterfaceSummary shape exactly.
        DECLARE @summary_json NVARCHAR(MAX) = (
            SELECT @interface_status AS [interface_status],
                   ISNULL(@updated_total, 0) AS [updated_item_count],
                   ISNULL(@skipped_total, 0) AS [skipped_item_count],
                   ISNULL(@already_total, 0) AS [already_processed_item_count],
                   JSON_QUERY((
                       SELECT
                           p.page_id AS [page_id],
                           p.receipt_detail_id AS [receipt_detail_id],
                           p.status AS [status],
                           p.reason AS [reason],
                           p.updated_item_count AS [updated_item_count],
                           p.skipped_item_count AS [skipped_item_count],
                           p.already_processed_item_count AS [already_processed_item_count],
                           JSON_QUERY((
                               SELECT i.seq AS [seq], i.parameter_name AS [parameter_name], i.status AS [status], i.reason AS [reason]
                               FROM @items AS i
                               WHERE i.page_id = p.page_id
                               ORDER BY i.seq
                               FOR JSON PATH
                           )) AS [items]
                       FROM @pages AS p
                       ORDER BY p.page_id
                       FOR JSON PATH
                   )) AS [pages]
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
        );

        UPDATE [dbo].[t_interface_lims_ocr_callback]
        SET [is_interface] = @completed,
            [interface_status] = @interface_status,
            [interface_summary_json] = @summary_json
        WHERE [callback_id] = @callback_id;

        IF @started_tran = 1 COMMIT TRANSACTION;

        SELECT @summary_json AS summary_json, CAST(0 AS BIT) AS is_replay;
    END TRY
    BEGIN CATCH
        IF @started_tran = 1 AND XACT_STATE() <> 0
            ROLLBACK TRANSACTION;

        THROW;
    END CATCH
END
GO
