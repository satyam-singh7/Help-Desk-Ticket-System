using System.ComponentModel.DataAnnotations;

namespace Help_Desk_Ticket_System.Models
{
    public class Ticket
    {
        public int Id { get; set; }

        [Required]
        public string Title { get; set; }

        [Required]
        public string Description { get; set; }

        [Required]
        public string Priority { get; set; } 

        [Required]
        public string Category { get; set; }

        public string Status { get; set; } = "Open"; 
        public DateTime DateSubmitted { get; set; } = DateTime.Now;

        public int UserId { get; set; }
        public User User { get; set; }

        public int? AssignedAdminId { get; set; }
        public User? AssignedAdmin { get; set; }

        public List<Comment> Comments { get; set; } = new List<Comment>();
    }
}
