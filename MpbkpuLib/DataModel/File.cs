using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MpbkpuLib.DataModel
{
    public class File : BaseModel
    {
        public enum FileTyes
        {
            File,
            Directory
        }

        public enum Actions
        {
            None = 0,
            Delete,
            Backup
        }

        public FileTyes Type { get; set; }

        /// <summary>
        /// The root path of the file (i.e. the source or destination folder of the backup)
        /// </summary>
        /// <value></value>
        [MaxLength(255)]
        public string? RootPath { get; set; }

        /// <summary>
        /// The path of the file  relative to RootPath
        /// </summary>
        /// <value></value>
        [MaxLength(255)]
        public string? RelativePath { get; set; }

        [MaxLength(255)]
        public string? FullPath { get; set; }

        [MaxLength(255)]
        public string? Name { get; set; }
        [MaxLength(255)]
        public string? Extension { get; set; }

        public long Size { get; set; }
        public bool Hidden { get; set; }
        public DateTime? Created { get; set; }
        public DateTime? LastModified { get; set; }

        public Actions Action { get; set; }
    }
}
