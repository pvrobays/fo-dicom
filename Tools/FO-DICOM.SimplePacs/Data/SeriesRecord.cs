// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FellowOakDicom.SimplePacs.Data
{
    /// <summary>
    /// Represents one DICOM series in the SimplePacs index.
    /// Stores the PS3.18 Table 10.6.3-4 required QIDO return attributes at series level.
    /// </summary>
    [Table("Series")]
    public class SeriesRecord
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        /// <summary>Foreign key to <see cref="StudyRecord"/>.</summary>
        public long StudyId { get; set; }

        /// <summary>(0020,000E) Series Instance UID. Unique index.</summary>
        [Required]
        [MaxLength(64)]
        public string SeriesInstanceUid { get; set; } = string.Empty;

        /// <summary>(0008,0060) Modality.</summary>
        [MaxLength(16)]
        public string? Modality { get; set; }

        /// <summary>(0008,103E) Series Description.</summary>
        [MaxLength(64)]
        public string? SeriesDescription { get; set; }

        /// <summary>(0020,0011) Series Number.</summary>
        public int? SeriesNumber { get; set; }

        /// <summary>(0020,0009) Number of Series Related Instances. Recomputed on every STOW.</summary>
        public int NumberOfSeriesRelatedInstances { get; set; }

        // Navigation
        [ForeignKey(nameof(StudyId))]
        public StudyRecord? Study { get; set; }

        public ICollection<InstanceRecord> Instances { get; set; } = new List<InstanceRecord>();
    }
}
