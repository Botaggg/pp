using System.ComponentModel.DataAnnotations;

namespace Callout.Web.Models;

public class RequestViewModel
{
    [Required(ErrorMessage = "Please enter your name.")]
    [StringLength(120)]
    [Display(Name = "Name")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter a phone number.")]
    [Phone(ErrorMessage = "That does not look like a phone number.")]
    [StringLength(40)]
    [Display(Name = "Phone")]
    public string Phone { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter an email address.")]
    [EmailAddress(ErrorMessage = "That does not look like an email address.")]
    [StringLength(200)]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter the address where the work is needed.")]
    [StringLength(300)]
    [Display(Name = "Address")]
    public string Address { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please describe what you need help with.")]
    [StringLength(2000, MinimumLength = 5, ErrorMessage = "Please give a little more detail.")]
    [Display(Name = "What do you need?")]
    public string Needs { get; set; } = string.Empty;

    [StringLength(300)]
    [Display(Name = "Rough availability")]
    public string Availability { get; set; } = string.Empty;

    /// <summary>
    /// Honeypot. Hidden from real users with CSS, so a human never fills it in.
    /// Any submission with a value here is discarded.
    /// </summary>
    public string Website { get; set; } = string.Empty;
}
