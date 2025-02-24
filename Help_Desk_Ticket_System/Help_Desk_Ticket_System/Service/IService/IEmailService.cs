namespace Help_Desk_Ticket_System.Service.IService
{
    public interface IEmailService
    {
        void SendEmail(string to, string subject, string body);
    }
}
