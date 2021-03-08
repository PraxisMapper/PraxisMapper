using Microsoft.Extensions.DependencyInjection;

namespace CoreComponents
{
    //For Dependency Injection?
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
