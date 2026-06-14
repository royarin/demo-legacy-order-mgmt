using LegacyOrderMgmt.Core.Data;
using LegacyOrderMgmt.Core.Models;
using LegacyOrderMgmt.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;

namespace LegacyOrderMgmt.Web.Controllers
{
    public class InvoiceController : Controller
    {
        private readonly OrderDbContext _db;
        private readonly InvoiceService _invoiceService;
        private readonly NotificationService _notificationService;
        private readonly ReportExportService _reportExportService;

        public InvoiceController(
            OrderDbContext db,
            InvoiceService invoiceService,
            NotificationService notificationService,
            ReportExportService reportExportService)
        {
            _db = db;
            _invoiceService = invoiceService;
            _notificationService = notificationService;
            _reportExportService = reportExportService;
        }

        public IActionResult Index(string paymentStatus)
        {
            var query = _db.Invoices.Include(i => i.Order).ThenInclude(o => o.Customer).AsQueryable();

            if (!string.IsNullOrEmpty(paymentStatus))
                query = query.Where(i => i.PaymentStatus == paymentStatus);

            var invoices = query.OrderByDescending(i => i.InvoiceDate).ToList();
            return View(invoices);
        }

        public IActionResult Details(int id)
        {
            var invoice = _db.Invoices
                .Include(i => i.Order).ThenInclude(o => o.Lines)
                .Include(i => i.Order).ThenInclude(o => o.Customer)
                .FirstOrDefault(i => i.Id == id);

            if (invoice == null) return NotFound();
            return View(invoice);
        }

        [HttpPost]
        public IActionResult Generate(int orderId)
        {
            var order = _db.Orders
                .Include(o => o.Lines)
                .Include(o => o.Customer)
                .FirstOrDefault(o => o.Id == orderId);

            if (order == null) return NotFound();

            // Legacy: synchronous invoice generation — no async, no background processing
            var invoice = _invoiceService.GenerateInvoice(order);
            _notificationService.SendInvoiceToCustomer(order.Customer, invoice);

            order.Status = 4;   // Invoiced
            order.UpdatedAt = DateTime.Now;
            _db.SaveChanges();

            return RedirectToAction("Details", new { id = invoice.Id });
        }

        [HttpPost]
        public IActionResult MarkPaid(int id, string paidBy)
        {
            var invoice = _db.Invoices.Find(id);
            if (invoice == null) return NotFound();

            invoice.PaymentStatus = "Paid";
            invoice.PaidDate = DateTime.Now;    // Legacy: DateTime.Now instead of UTC
            invoice.PaidBy = paidBy;
            _db.SaveChanges();

            return RedirectToAction("Details", new { id });
        }

        public IActionResult OverdueReport()
        {
            // Legacy: raw SQL string for date comparison — not parameterized
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var sql = "SELECT * FROM Invoices WHERE PaymentStatus = 'Pending' AND DueDate < '" + today + "'";
            var overdue = _db.Invoices.FromSql(sql).Include(i => i.Order).ToList();
            return View(overdue);
        }

        [HttpGet("reports/sales-csv")]
        public IActionResult SalesCsv(DateTime? from, DateTime? to)
        {
            var fromDate = from ?? DateTime.Now.AddDays(-30);
            var toDate = to ?? DateTime.Now;
            var csv = _reportExportService.GenerateSalesReportCsv(fromDate, toDate);
            var fileName = $"sales_{fromDate:yyyyMMdd}_{toDate:yyyyMMdd}.csv";
            return File(csv, "text/csv", fileName);
        }
    }
}
