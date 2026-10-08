using System.ComponentModel.DataAnnotations;

namespace DlangezwaHS.Web.ViewModels;

public class ContactViewModel
{
    [Required, StringLength(100), Display(Name = "Full name")]
    public string Name { get; set; } = "";

    [Required, EmailAddress, StringLength(150)]
    public string Email { get; set; } = "";

    [Phone, StringLength(30), Display(Name = "Phone (optional)")]
    public string? Phone { get; set; }

    [Required]
    public string Topic { get; set; } = "General enquiry";

    [Required, StringLength(2000, MinimumLength = 10)]
    public string Message { get; set; } = "";
}
