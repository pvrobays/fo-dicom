// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FellowOakDicom.SimplePacs.Data
{
    /// <summary>
    /// Represents one DICOM instance (SOP Instance) in the SimplePacs index.
    /// Stores the PS3.18 Table 10.6.3-5 required QIDO return attributes at instance level.
    /// </summary>
    [Table("Instances")]
    public class InstanceRecord
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        /// <summary>Foreign key to <see cref="SeriesRecord"/>.</summary>
        public long SeriesId { get; set; }

        /// <summary>(0008,0018) SOP Instance UID. Unique index.</summary>
        [Required]
        [MaxLength(64)]
        public string SopInstanceUid { get; set; } = string.Empty;

        /// <summary>(0008,0016) SOP Class UID.</summary>
        [MaxLength(64)]
        public string? SopClassUid { get; set; }

        /// <summary>(0020,0013) Instance Number.</summary>
        public int? InstanceNumber { get; set; }

        /// <summary>Transfer Syntax UID from the file meta information.</summary>
        [MaxLength(64)]
        public string? TransferSyntaxUid { get; set; }

        /// <summary>
        /// Path to the .dcm file on disk, relative to the storage root.
        /// Format: {StudyInstanceUID}/{SopInstanceUID}.dcm
        /// </summary>
        [Required]
        [MaxLength(256)]
        public string FilePath { get; set; } = string.Empty;

        // Navigation
        [ForeignKey(nameof(SeriesId))]
        public SeriesRecord? Series { get; set; }
    }
}
