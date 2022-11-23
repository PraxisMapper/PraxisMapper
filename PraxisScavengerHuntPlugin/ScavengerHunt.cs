namespace PraxisScavengerHuntPlugin
{
    public class ScavengerHunt
    {
            public long id { get; set; }
            public string name { get; set; }
            public ICollection<ScavengerHuntEntry> entries { get; set; }
    }

    public class ScavengerHuntEntry
    {
        public long id { get; set; }
        public ScavengerHunt ScavengerHunt { get; set; }
        public string description { get; set; }
        public string StoredOsmElementId { get; set; } //This means we have to add a point/polygon if it's not an existing OSM entry.
        public Guid PrivacyId { get; set; }
        //public Place Place { get; set; }
    }
}
