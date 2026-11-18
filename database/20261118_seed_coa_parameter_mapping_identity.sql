-- Seed identity mappings (ocr_parameter_name = coa_parameter_name) for COA parameters
-- that are not yet the target of any row in t_lims_coa_parameter_mapping.
--
-- Skipped on purpose:
--   * COA names that already exist as an ocr_parameter_name in the mapping
--     (inserting them would create a second mapping for the same OCR name and
--     make the remap return mapping_ambiguous).
--   * Rows with a blank name.
SET NOCOUNT ON;

INSERT INTO [dbo].[t_lims_coa_parameter_mapping]
    ([coa_mapping_parameter_id], [coa_parameter_name], [ocr_parameter_name],
     [is_active], [create_by], [create_date])
SELECT NEWID(), src.[coa_name], src.[coa_name], 'YES', 'system', GETDATE()
FROM
(
    SELECT DISTINCT LTRIM(RTRIM(p.[coa_parameter_name])) AS [coa_name]
    FROM [dbo].[t_lims_coa_parameter] p
    WHERE p.[coa_parameter_name] IS NOT NULL
      AND LTRIM(RTRIM(p.[coa_parameter_name])) <> ''
      AND NOT EXISTS
      (
          SELECT 1 FROM [dbo].[t_lims_coa_parameter_mapping] m
          WHERE LTRIM(RTRIM(m.[coa_parameter_name])) = LTRIM(RTRIM(p.[coa_parameter_name]))
      )
      AND NOT EXISTS
      (
          SELECT 1 FROM [dbo].[t_lims_coa_parameter_mapping] m
          WHERE LTRIM(RTRIM(m.[ocr_parameter_name])) = LTRIM(RTRIM(p.[coa_parameter_name]))
      )
) AS src;

SELECT @@ROWCOUNT AS inserted_rows;
