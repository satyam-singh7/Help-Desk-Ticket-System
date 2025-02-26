using Help_Desk_Ticket_System.Data;
using Help_Desk_Ticket_System.Models;
using Help_Desk_Ticket_System.Service.IService;
using iTextSharp.text.pdf;
using iTextSharp.text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;

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
            Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Expires"] = "0";
            var role = HttpContext.Session.GetString("UserRole");
            if (role != "Admin")
            {
                return RedirectToAction("Login", "Account");
            }
            var tickets = _context.Tickets
        .Include(t => t.AssignedAdmin)
        .Include(t=>t.User)
        .AsQueryable();
      
            if (!string.IsNullOrEmpty(search))
            {
                tickets = tickets.Where(t => t.Title.Contains(search));
            }

            if (!string.IsNullOrEmpty(status))
            {
                tickets = tickets.Where(t => t.Status == status);
            }

            var statuses = _context.Tickets
                .Select(t => t.Status)
                .Distinct()
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            ViewBag.Statuses = new SelectList(statuses);

            var ticket = tickets.ToList();           
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(tickets);
            }

            return View(tickets);
        }
        [HttpGet]
        public JsonResult GetTickets()
        {
            var tickets = _context.Tickets
                .Include(t => t.AssignedAdmin)
                .Include(t => t.User)
                .Select(t => new
                {
                    t.Id,
                    t.Title,
                    t.Description,
                    t.Priority,
                    t.Status,
                    t.Category,
                    UserName = t.User != null ? t.User.Name : "Unknown",
                    AssignedAdmin = t.AssignedAdmin != null ? t.AssignedAdmin.Name : "Not Assigned",
                    DateSubmitted = t.DateSubmitted.ToString("yyyy-MM-dd")
                })
                .ToList();

            return Json(tickets);
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
            Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Expires"] = "0";           
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
        public IActionResult AdminDashboard(string status, string priority, int? assignedAdmin)
        {
            

            var tickets = _context.Tickets
                .Include(t => t.AssignedAdmin)
                .AsQueryable();

            
            if (!string.IsNullOrEmpty(status))
            {
                tickets = tickets.Where(t => t.Status == status);
            }

            if (!string.IsNullOrEmpty(priority))
            {
                tickets = tickets.Where(t => t.Priority == priority);
            }

            if (assignedAdmin.HasValue)
            {
                tickets = tickets.Where(t => t.AssignedAdminId == assignedAdmin);
            }

            ViewBag.TotalTickets = _context.Tickets.Count();
            ViewBag.PendingTickets = _context.Tickets.Count(t => t.Status == "Pending");
            ViewBag.ResolvedTickets = _context.Tickets.Count(t => t.Status == "Resolved");

            ViewBag.TicketsByCategory = _context.Tickets
                .GroupBy(t => t.Category)
                .Select(g => new { Category = g.Key, Count = g.Count() })
                .ToList();

            ViewBag.TicketsResolvedPerAdmin = _context.Tickets
                .Where(t => t.Status == "Resolved" && t.AssignedAdminId != null)
                .GroupBy(t => t.AssignedAdmin.Name)
                .Select(g => new { Admin = g.Key, Count = g.Count() })
                .ToList();
            ViewBag.Statuses = new SelectList(_context.Tickets.Select(t => t.Status).Distinct().ToList());
            ViewBag.Priorities = new SelectList(_context.Tickets.Select(t => t.Priority).Distinct().ToList());
            ViewBag.Admins = new SelectList(_context.Users.Where(u => u.Role == "Admin"), "Id", "Name");

            return View(tickets.ToList());
        }
        [HttpGet]
        public IActionResult ExportUserTicketToExcel(int id)
        {
            if (id <= 0)
            {
                return BadRequest("Invalid Ticket ID.");
            }

            var ticket = _context.Tickets.FirstOrDefault(t => t.Id == id);

            if (ticket == null)
            {
                return NotFound($"Ticket with ID {id} not found.");
            }

            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            using (var package = new ExcelPackage())
            {
                var worksheet = package.Workbook.Worksheets.Add($"Ticket_{ticket.Id}");

                worksheet.Cells[1, 1].Value = "Ticket ID";
                worksheet.Cells[1, 2].Value = "Title";
                worksheet.Cells[1, 3].Value = "Description";
                worksheet.Cells[1, 4].Value = "Priority";
                worksheet.Cells[1, 5].Value = "Status";
                worksheet.Cells[1, 6].Value = "Date Submitted";

                worksheet.Cells[2, 1].Value = ticket.Id;
                worksheet.Cells[2, 2].Value = ticket.Title;
                worksheet.Cells[2, 3].Value = ticket.Description;
                worksheet.Cells[2, 4].Value = ticket.Priority;
                worksheet.Cells[2, 5].Value = ticket.Status;
                worksheet.Cells[2, 6].Value = ticket.DateSubmitted.ToString("yyyy-MM-dd");

                worksheet.Cells.AutoFitColumns();

                var stream = new MemoryStream(package.GetAsByteArray());
                string fileName = $"Ticket_{ticket.Id}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
        }

        [HttpGet]
        public IActionResult DownloadTicketPdf(int id)
        {
            Console.WriteLine($"DownloadTicketPdf called with ID: {id}");

            if (id <= 0)
            {
                return BadRequest("Invalid Ticket ID.");
            }

            var ticket = _context.Tickets
                .Include(t => t.User)
                .Include(t => t.AssignedAdmin)
                .Include(t => t.Comments)
                .ThenInclude(c => c.User)
                .FirstOrDefault(t => t.Id == id);

            if (ticket == null)
            {
                return NotFound($"Ticket with ID {id} not found.");
            }

            using (MemoryStream stream = new MemoryStream())
            {
                Document document = new Document(PageSize.A4, 25, 25, 30, 30);
                PdfWriter.GetInstance(document, stream);
                document.Open();               
                Font titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 18);
                Paragraph title = new Paragraph("Ticket Details\n\n", titleFont)
                {
                    Alignment = Element.ALIGN_CENTER
                };
                document.Add(title);            
                Font labelFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12);
                Font valueFont = FontFactory.GetFont(FontFactory.HELVETICA, 12);              
                document.Add(new Paragraph($"Title: {ticket.Title}", labelFont));
                document.Add(new Paragraph($"Description: {ticket.Description}", valueFont));
                document.Add(new Paragraph($"Priority: {ticket.Priority}", valueFont));
                document.Add(new Paragraph($"Status: {ticket.Status}", valueFont));
                document.Add(new Paragraph($"Assigned Admin: {(ticket.AssignedAdmin != null ? ticket.AssignedAdmin.Name : "Not Assigned")}", valueFont));
                document.Add(new Paragraph($"Date Submitted: {ticket.DateSubmitted:yyyy-MM-dd HH:mm}\n\n", valueFont));            
                if (ticket.Comments.Any())
                {
                    document.Add(new Paragraph("Comments:\n", labelFont));
                    foreach (var comment in ticket.Comments)
                    {
                        document.Add(new Paragraph($"[{comment.DatePosted:yyyy-MM-dd HH:mm}] {comment.User.Name}: {comment.TicketComment}", valueFont));
                    }
                }
                else
                {
                    document.Add(new Paragraph("No comments available.", valueFont));
                }

                document.Close();              
                byte[] pdfBytes = stream.ToArray();

                string fileName = $"Ticket_{ticket.Id}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
                return File(pdfBytes, "application/pdf", fileName);
            }
        }



    }
}
