// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FellowOakDicom.SimplePacs.Data
{
    /// <summary>
    /// Represents one DICOM study in the SimplePacs index.
    /// Stores the PS3.18 Table 10.6.1-2 required QIDO return attributes at study level.
    /// </summary>
    [Table("Studies")]
    public class StudyRecord
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        /// <summary>(0020,000D) Study Instance UID. Unique index.</summary>
        [Required]
        [MaxLength(64)]
        public string StudyInstanceUid { get; set; } = string.Empty;

        /// <summary>(0008,0020) Study Date (DA format e.g. "20230101").</summary>
        [MaxLength(8)]
        public string? StudyDate { get; set; }

        /// <summary>(0008,0030) Study Time (TM format e.g. "120000.000").</summary>
        [MaxLength(16)]
        public string? StudyTime { get; set; }

        /// <summary>(0008,0050) Accession Number.</summary>
        [MaxLength(16)]
        public string? AccessionNumber { get; set; }

        /// <summary>(0008,0090) Referring Physician Name.</summary>
        [MaxLength(64)]
        public string? ReferringPhysicianName { get; set; }

        /// <summary>(0010,0010) Patient Name (formatted as LAST^FIRST).</summary>
        [MaxLength(64)]
        public string? PatientName { get; set; }

        /// <summary>(0010,0020) Patient ID.</summary>
        [MaxLength(64)]
        public string? PatientId { get; set; }

        /// <summary>(0010,0030) Patient Birth Date (DA format).</summary>
        [MaxLength(8)]
        public string? PatientBirthDate { get; set; }

        /// <summary>(0010,0040) Patient Sex.</summary>
        [MaxLength(16)]
        public string? PatientSex { get; set; }

        /// <summary>(0020,0010) Study ID.</summary>
        [MaxLength(16)]
        public string? StudyId { get; set; }

        /// <summary>
        /// (0008,0061) Modalities In Study — comma-separated list of modalities
        /// across all series. Recomputed on every STOW.
        /// </summary>
        [MaxLength(256)]
        public string? ModalitiesInStudy { get; set; }

        /// <summary>(0020,1206) Number of Study Related Series. Recomputed on every STOW.</summary>
        public int NumberOfStudyRelatedSeries { get; set; }

        /// <summary>(0020,1208) Number of Study Related Instances. Recomputed on every STOW.</summary>
        public int NumberOfStudyRelatedInstances { get; set; }

        // Navigation
        public ICollection<SeriesRecord> Series { get; set; } = new List<SeriesRecord>();
    }
}
