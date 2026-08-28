-- Add encrypted e-conomic tokens per organization (Key Vault / Data Protection)
-- Tokens are stored encrypted, never plain. Null = demo / ingen integration.

IF COL_LENGTH('Organizations', 'EconomicsAgreementGrantTokenEncrypted') IS NULL
BEGIN
    ALTER TABLE Organizations ADD EconomicsAgreementGrantTokenEncrypted NVARCHAR(MAX) NULL;
END

IF COL_LENGTH('Organizations', 'EconomicsAppSecretTokenEncrypted') IS NULL
BEGIN
    ALTER TABLE Organizations ADD EconomicsAppSecretTokenEncrypted NVARCHAR(MAX) NULL;
END
