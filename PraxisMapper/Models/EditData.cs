using Microsoft.AspNetCore.Mvc.Rendering;
using System.Collections.Generic;

namespace PraxisMapper.Models
{
    public class EditData
    {
        //The model for the AdminView/EditData page
        public string accessKey;
        public string currentGlobalKey;
        public List<SelectListItem> globalDataKeys;
    }
}
