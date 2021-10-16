using Microsoft.AspNetCore.Mvc.Rendering;
using System.Collections.Generic;

namespace PraxisMapper.Models
{
    public class EditData
    {
        //The model for the AdminView/EditData page
        public string accessKey;
        public string currentGlobalKey;
        public string currentPlayerKey;
        public List<SelectListItem> globalDataKeys;
        public List<SelectListItem> playerKeys;
        public string currentStylesetKey;
        public List<SelectListItem> stylesetKeys;
    }
}
