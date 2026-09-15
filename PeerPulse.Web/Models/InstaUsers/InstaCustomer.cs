using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PeerPulse.Web.Models.InstaUsers;

[Table("InstaCustomer", Schema = "dbo")]
public partial class InstaCustomer
{
    [Key]
    [Column("InstaCustomerID")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int InstaCustomerId { get; set; }

    [MaxLength(100)]
    public string? CustomerName { get; set; }

    [MaxLength(100)]
    public string? CustomerEmail { get; set; }

    [MaxLength(200)]
    public string? Organization { get; set; }

    [MaxLength(100)]
    public string? Department { get; set; }

    [MaxLength(100)]
    public string? Designation { get; set; }

    [MaxLength(20)]
    public string? ContactNo { get; set; }

    [Column("ContactNo_Old")]
    [MaxLength(20)]
    public string? ContactNoOld { get; set; }

    [Column(TypeName = "varchar(200)")]
    public string? StateName { get; set; }

    [Column(TypeName = "varchar(200)")]
    public string? District { get; set; }

    [Column("StatusID")]
    public int? StatusId { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? CreatedOn { get; set; }

    [Column("InstaGroupID")]
    public int? InstaGroupId { get; set; }

    [Column(TypeName = "nvarchar(max)")]
    public string? Address { get; set; }

    [Column(TypeName = "varchar(6)")]
    public string? Pincode { get; set; }

    [Column("GSTIN")]
    [MaxLength(15)]
    public string? Gstin { get; set; }

    [Column("Count_Reg")]
    public int? CountReg { get; set; }

    public int? Gender { get; set; }

    [MaxLength(100)]
    public string? CountryName { get; set; }

    [Column(TypeName = "nvarchar(max)")]
    public string? BillingAddress { get; set; }

    [Column(TypeName = "varchar(50)")]
    public string? BillingPincode { get; set; }

    [Column(TypeName = "varchar(200)")]
    public string? BillingStateName { get; set; }

    [Column(TypeName = "varchar(200)")]
    public string? BillingCountryName { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? LastUpdatedOn { get; set; }

    public bool? IsCompanyInfoAvl { get; set; }

    [Column(TypeName = "varchar(100)")]
    public string? RegStateName { get; set; }

    [Column(TypeName = "varchar(200)")]
    public string? RegCountryName { get; set; }

    [Column(TypeName = "varchar(100)")]
    public string? BusinessStatus { get; set; }

    [MaxLength(500)]
    public string? LegalName { get; set; }

    [Column("IsGSTActive")]
    [MaxLength(30)]
    public string? IsGstActive { get; set; }

    [Column("IsGSTVerifiedByCust")]
    public bool? IsGstVerifiedByCust { get; set; }

    [Column("GSTVerifiedDate", TypeName = "datetime")]
    public DateTime? GstVerifiedDate { get; set; }

    [Column("IsGSTApplicable")]
    public bool? IsGstApplicable { get; set; }

    public bool? IsGstVerifiedManual { get; set; }

    [Column("WebsiteURL", TypeName = "nvarchar(max)")]
    public string? WebsiteUrl { get; set; }

    [Column("InstaFinancialsURL", TypeName = "nvarchar(max)")]
    public string? InstaFinancialsUrl { get; set; }

    [MaxLength(500)]
    public string? Industry { get; set; }

    [Column("SourceofContact")]
    [MaxLength(500)]
    public string? SourceOfContact { get; set; }

    [MaxLength(500)]
    public string? CustomerType { get; set; }

    [MaxLength(500)]
    public string? UseCase { get; set; }

    [MaxLength(500)]
    public string? ProductsOne { get; set; }

    [MaxLength(500)]
    public string? ProductsTwo { get; set; }

    [MaxLength(200)]
    public string? AccountOwner { get; set; }

    [MaxLength(500)]
    public string? ContactStatus { get; set; }

    [Column(TypeName = "date")]
    public DateOnly? FirstContactDate { get; set; }

    [Column(TypeName = "date")]
    public DateOnly? NextContactDate { get; set; }

    [Column(TypeName = "nvarchar(max)")]
    public string? NextActionPlan { get; set; }

    [Column(TypeName = "nvarchar(max)")]
    public string? ActionSummary { get; set; }

    [Column(TypeName = "nvarchar(max)")]
    public string? Notes { get; set; }
}