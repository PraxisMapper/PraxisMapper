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

            services.AddDbContext<PraxisContext>(ServiceLifetime.Scoped, ServiceLifetime.Scoped);
            //Add other services here.

            return services;
        }
        
    }
}
