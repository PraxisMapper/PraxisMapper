using Microsoft.EntityFrameworkCore;
using PraxisCore;

namespace PraxisScavengerHuntPlugin
{
    public class ScavengerHuntContext : PraxisContext
    {
        public DbSet<ScavengerHunt> ScavengerHunts { get; set; }
        public DbSet<ScavengerHuntEntry> ScavengerHuntEntries { get; set; }

        public void CheckAndCreateTables()
        {
            //TODO: other modes / generic, works-for-all logic.
            if (serverMode == "MariaDB")
            {
                //TODO: confirm this is the right SQL for this with EF. Do I need a Join table?
                Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS ScavengerHunts( id BIGINT NOT NULL AUTO_INCREMENT, name VARCHAR(MAX), PRIMARY KEY (`id`) USING BTREE );");
                Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS ScavengerHuntEntries( id BIGINT NOT NULL AUTO_INCREMENT, description VARCHAR(MAX), ScavengerHuntId BIGINT, PrivacyID CHAR(36), PRIMARY KEY (`id`) USING BTREE, INDEX `HuntIndex` (`ScavengerHuntId`) USING BTREE, INDEX `PlaceIndex` (`StoredOsmElementId`) USING BTREE, CONSTRAINT `FK_scavengerhunt_places` FOREIGN KEY (`PrivacyId`) REFERENCES `praxis`.`places` (`PrivacyId`) ON UPDATE RESTRICT ON DELETE RESTRICT  );");
            }
        }
    }
}
