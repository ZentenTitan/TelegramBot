using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.DBMS
{
    class ApplicationContext : DbContext
    {
        public DbSet<Debtor> Debtors { get; set; }
        public ApplicationContext()
        {
            Database.EnsureCreated();
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlServer(@"Server=(localdb)\mssqllocaldb;Database=FinancialSystem;Trusted_Connection=True;");
        }
    }
}
