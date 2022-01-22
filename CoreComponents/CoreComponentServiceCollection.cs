using Microsoft.Extensions.DependencyInjection;

namespace PraxisCore
{
    //For Dependency Injection
    //NOTE: this works as written, but I have no particular purpose assigned to this service yet. 
    public static class CoreComponentServiceCollectionExtension
    {

        public static IServiceCollection AddCoreComponentServiceCollection(this IServiceCollection services)
        {
            //create services? 

            //This might be the best place to handle loading different variant DLLs on startup to pass them in to classes.
            services.AddDbContext<PraxisContext>(ServiceLifetime.Scoped, ServiceLifetime.Scoped);

            if (true)

            //Add other services here.
            return services;
        }
        
    }
}
