using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace DatabaseAccess
{
    //For Dependency Injection?
    public static class GpsAppServiceCollectionExtension
    {

        public static IServiceCollection AddGpsAppServiceCollection(this IServiceCollection services)
        {
            //create services? 

            services.AddDbContext<GpsExploreContext>(ServiceLifetime.Scoped, ServiceLifetime.Scoped);
            //Add other services here.

            return services;
        }
        
    }
}
