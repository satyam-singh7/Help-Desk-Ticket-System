using System.ComponentModel.DataAnnotations;

namespace Help_Desk_Ticket_System.Models
{
    public class Comment
    {
        public int Id { get; set; }
        public int TicketId { get; set; }
        public Ticket Ticket { get; set; }

        public int UserId { get; set; }
        public User User { get; set; }
        [Required(ErrorMessage = "Comment cannot be empty.")]
        public string TicketComment { get; set; }
        public DateTime DatePosted { get; set; } = DateTime.Now;
    }
}
