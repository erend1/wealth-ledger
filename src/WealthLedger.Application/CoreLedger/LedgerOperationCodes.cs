namespace WealthLedger.Application.CoreLedger
{
    public static class LedgerOperationCodes
    {
        public const string RecordContribution = "RECORD_CONTRIBUTION";

        public const string RecordFundPurchase = "RECORD_FUND_PURCHASE";

        public const string RecordFundSale = "RECORD_FUND_SALE";

        public const string RecordPhysicalGoldPurchase =
            "RECORD_PHYSICAL_GOLD_PURCHASE";

        public const string RecordPhysicalGoldSale =
            "RECORD_PHYSICAL_GOLD_SALE";

        public const string RecordPhysicalGoldTransfer =
            "RECORD_PHYSICAL_GOLD_TRANSFER";

        public const string RecordOpeningBalance = "RECORD_OPENING_BALANCE";

        public const string ReversePostedTransaction = "REVERSE_POSTED_TRANSACTION";
    }
}
