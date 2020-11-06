using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

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
