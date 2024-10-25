using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Web;

namespace yillikizin
{
    public class YillikizinEntities : DbContext
    {
        public DbSet<Departman> Departman { get; set; } // İsimleri büyük harfle yazıyoruz
        public DbSet<Personel> Personel { get; set; }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Personel>()
                .HasRequired(p => p.Departman)
                .WithMany(d => d.Personeller)
                .HasForeignKey(p => p.DepartmanId);
        }
    }
}
