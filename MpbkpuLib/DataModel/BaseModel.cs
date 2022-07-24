using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MpbkpuLib.DataModel
{
    public abstract class BaseModel
    {
        [Required]
        public int Id { get; set; }
    }
}
