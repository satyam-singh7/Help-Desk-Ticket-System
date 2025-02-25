using Help_Desk_Ticket_System.Data;
using Help_Desk_Ticket_System.Models;
using Help_Desk_Ticket_System.Service.IService;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Help_Desk_Ticket_System.Controllers
{
    public class TicketController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IEmailService _emailService;
        public TicketController(ApplicationDbContext context, IEmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }
        public IActionResult Index(string search, string status)
        {
            var tickets = _context.Tickets
        .Include(t => t.AssignedAdmin)
        .Include(t=>t.User)
        .AsQueryable();
            //.ToList();

            if (!string.IsNullOrEmpty(search))
            {
                tickets = tickets.Where(t => t.Title.Contains(search));
            }

            if (!string.IsNullOrEmpty(status))
            {
                tickets = tickets.Where(t => t.Status == status);
            }

            var statuses = _context.Tickets.Select(t => t.Status).Distinct().ToList();
            ViewBag.Statuses = new SelectList(statuses);

            return View(tickets.ToList());
        }
        public IActionResult Details(int id)
        {
            var ticket = _context.Tickets
                .Include(t => t.User)
                .Include(t => t.AssignedAdmin)
                .Include(t => t.Comments)
                .ThenInclude(c => c.User) 
                .FirstOrDefault(t => t.Id == id);

            if (ticket == null)
            {
                return NotFound();
            }

           
            var admins = _context.Users.Where(u => u.Role == "Admin").ToList();
            ViewBag.Admins = new SelectList(admins, "Id", "Name");

            return View(ticket);
        }

        public IActionResult MyTickets(string search, string status)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var tickets = _context.Tickets
                .Include(t => t.Comments)
                .Include(t => t.AssignedAdmin)
                .Where(t => t.UserId == userId);

            if (!string.IsNullOrEmpty(search))
            {
                tickets = tickets.Where(t => t.Title.Contains(search));
            }

            if (!string.IsNullOrEmpty(status))
            {
                tickets = tickets.Where(t => t.Status == status);
            }

            return View(tickets.ToList());
        }

        [HttpPost]
        public IActionResult AddComment([FromBody] CommentRequest request)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
            {
                return Json(new { success = false, message = "User not logged in" });
            }

           
            string cleanComment = System.Text.RegularExpressions.Regex.Replace(request.CommentText, "<.*?>", "");

            var comment = new Comment
            {
                TicketId = request.TicketId,
                UserId = userId.Value,
                TicketComment = cleanComment,
                DatePosted = DateTime.Now
            };

            _context.Comments.Add(comment);
            _context.SaveChanges();

            var user = _context.Users.Find(userId);
            return Json(new
            {
                success = true,
                user = user.Name,
                comment = comment.TicketComment,
                date = comment.DatePosted.ToString("yyyy-MM-dd HH:mm")
            });
        }

        public class CommentRequest
        {
            public int TicketId { get; set; }
            public string CommentText { get; set; }
        }

        public IActionResult Create()
        {
            var role = HttpContext.Session.GetString("UserRole");
            if (role != "User")
            {
                return RedirectToAction("Login", "Account");
            }

            return View();
        }
        [HttpPost]
        public IActionResult Create(Ticket ticket)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
            {
                return RedirectToAction("Login", "Account");
            }

            ticket.UserId = userId.Value;
            ticket.Status = "Open";
            ticket.DateSubmitted = DateTime.Now;

            _context.Tickets.Add(ticket);
            _context.SaveChanges();
            return RedirectToAction("MyTickets");
        }
        public IActionResult Edit(int id)
        {
            var ticket = _context.Tickets.Find(id);
            if (ticket == null)
            {
                return NotFound();
            }

            ViewBag.Admins = _context.Users.Where(u => u.Role == "Admin").ToList();
            return View(ticket);
        }


        [HttpPost]
        public IActionResult Edit(Ticket model)
        {           
            var ticket = _context.Tickets.Find(model.Id);
            if (ticket == null)
            {
                return NotFound();
            }
            bool isAssignedAdminChanged = ticket.AssignedAdminId != model.AssignedAdminId;
            bool isStatusChanged = ticket.Status != model.Status;

            ticket.Title = model.Title;
            ticket.Description = model.Description;
            ticket.Priority = model.Priority;
            ticket.Category = model.Category;
            ticket.Status = model.Status;
            ticket.AssignedAdminId = model.AssignedAdminId;

            _context.SaveChanges();

            var admin = _context.Users.Find(ticket.AssignedAdminId);
            var user = _context.Users.Find(ticket.UserId);            
            if (isAssignedAdminChanged && admin != null)
            {
                string subject = "New Ticket Assigned";
                string body = $"Dear {admin.Name},<br><br>A new ticket has been assigned to you.<br>" +
                              $"<strong>Title:</strong> {ticket.Title}<br>" +
                              $"<strong>Priority:</strong> {ticket.Priority}<br>" +
                              $"<strong>Status:</strong> {ticket.Status}<br>" +
                              $"<strong>Category:</strong> {ticket.Category}<br>" +
                              $"Please check the system for more details.";

                _emailService.SendEmail(admin.Email, subject, body);
            }
            var userEmail = user?.Email;            
            if (user != null && (isAssignedAdminChanged || isStatusChanged))
            {
                string subject = "Ticket Status Updated";
                string body = $"Dear {user.Name},<br><br>Your ticket details  has been updated.<br>" +
                              $"<strong>Title:</strong> {ticket.Title}<br>" +
                              $"<strong>New Status:</strong> {ticket.Status}<br>" +
                              $"<strong>Assigned Admin:</strong> {(admin != null ? admin.Name : "None")}<br>"+
                              $"You can check the details in the system.";

                _emailService.SendEmail(user.Email, subject, body);
            }

            return RedirectToAction("Index");
        }
        public IActionResult AdminDashboard()
        {
            var totalTickets = _context.Tickets.Count();
            var pendingTickets = _context.Tickets.Count(t => t.Status == "Pending");
            var resolvedTickets = _context.Tickets.Count(t => t.Status == "Resolved");

            var ticketsByCategory = _context.Tickets
                .GroupBy(t => t.Category)
                .Select(g => new { Category = g.Key, Count = g.Count() })
                .ToList();

            var ticketsByAdmin = _context.Tickets
                .Where(t => t.AssignedAdminId != null)
                .GroupBy(t => t.AssignedAdmin.Name)
                .Select(g => new { Admin = g.Key, Count = g.Count() })
                .ToList();

            var statuses = _context.Tickets.Select(t => t.Status).Distinct().ToList();
            var priorities = _context.Tickets.Select(t => t.Priority).Distinct().ToList();
            var admins = _context.Users.Where(u => u.Role == "Admin").ToList();

            ViewBag.Statuses = new SelectList(statuses);
            ViewBag.Priorities = new SelectList(priorities);
            ViewBag.Admins = new SelectList(admins, "Id", "Name");

            return View(new
            {
                TotalTickets = totalTickets,
                PendingTickets = pendingTickets,
                ResolvedTickets = resolvedTickets,
                TicketsByCategory = ticketsByCategory,
                TicketsByAdmin = ticketsByAdmin
            });
        }

    }
}
