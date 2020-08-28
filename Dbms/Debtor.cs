namespace FinancialSystem.DBMS
{
    class Debtor
    {
        public int Id { get; set; }
        public long ChatId { get; set; }
        public int DebtorId { get; set; }
        public byte DebtorPhase { get; set; }
        public string DebtorUsername { get; set; }
        public string LenderUsername { get; set; }
        public ushort LoanAmount { get; set; }
    }
}
