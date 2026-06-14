using LegacyOrderMgmt.Core.Data;
using LegacyOrderMgmt.Core.Models;
using LegacyOrderMgmt.Core.Services;
using LegacyOrderMgmt.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;

namespace LegacyOrderMgmt.Web.Controllers
{
    public class OrdersController : Controller
    {
        private readonly OrderDbContext _db;
        private readonly OrderService _orderService;
        private readonly PricingEngine _pricingEngine;
        private readonly NotificationService _notificationService;
        private readonly ShippingIntegrationService _shippingService;

        public OrdersController(
            OrderDbContext db,
            OrderService orderService,
            PricingEngine pricingEngine,
            NotificationService notificationService,
            ShippingIntegrationService shippingService)
        {
            _db = db;
            _orderService = orderService;
            _pricingEngine = pricingEngine;
            _notificationService = notificationService;
            _shippingService = shippingService;
        }

        // Legacy: synchronous action, no async/await
        public IActionResult Index(string status, string search)
        {
            var query = _db.Orders.Include(o => o.Customer).AsQueryable();

            if (!string.IsNullOrEmpty(status) && int.TryParse(status, out int statusInt))
                query = query.Where(o => o.Status == statusInt);

            if (!string.IsNullOrEmpty(search))
            {
                // Legacy: case-sensitive LIKE via EF — no IQueryable extension or spec pattern
                query = query.Where(o =>
                    o.OrderNumber.Contains(search) ||
                    o.Customer.CompanyName.Contains(search));
            }

            // Legacy: no AsNoTracking() on list query
            var orders = query.OrderByDescending(o => o.OrderDate).ToList();
            return View(orders);
        }

        public IActionResult Details(int id)
        {
            // Legacy: tries cache first, falls back to DB — BinaryFormatter cache
            var order = _orderService.GetCachedOrder(id) ?? _orderService.GetOrderById(id);
            if (order == null) return NotFound();
            return View(order);
        }

        public IActionResult Create()
        {
            ViewBag.Customers = _db.Customers.Where(c => c.IsActive).ToList();
            return View();
        }

        [HttpPost]
        public IActionResult Create(int customerId)
        {
            var order = _orderService.CreateOrder(customerId, User.Identity.Name ?? "system");
            return RedirectToAction("Edit", new { id = order.Id });
        }

        public IActionResult Edit(int id)
        {
            var order = _orderService.GetOrderById(id);
            if (order == null) return NotFound();

            ViewBag.Products = _db.Products.Where(p => p.IsActive).ToList();
            return View(order);
        }

        [HttpPost]
        public IActionResult AddLine(int orderId, int productId, int quantity)
        {
            var order = _orderService.GetOrderById(orderId);
            var product = _db.Products.Find(productId);

            if (order == null || product == null)
                return BadRequest();

            // Legacy: pricing calculated inline in controller — no separation of concerns
            var customer = _db.Customers.Find(order.CustomerId);
            var discountPct = _pricingEngine.CalculateDiscount(order, customer?.CreditLimit ?? "Standard");

            var line = new OrderLine
            {
                OrderId = orderId,
                ProductId = productId,
                ProductCode = product.ProductCode,
                ProductName = product.Name,
                Quantity = quantity,
                UnitPrice = product.UnitPrice,
                DiscountPercent = discountPct,
                LineTotal = _pricingEngine.CalculateLineTotal(quantity, product.UnitPrice, discountPct),
                LineStatus = 0
            };

            order.Lines.Add(line);
            _pricingEngine.RecalculateOrderTotals(order, customer?.CreditLimit ?? "Standard");
            _db.SaveChanges();

            return RedirectToAction("Edit", new { id = orderId });
        }

        [HttpPost]
        public IActionResult Confirm(int id)
        {
            _orderService.ConfirmOrder(id);

            // Legacy: fire-and-forget notification with no error handling
            var order = _orderService.GetOrderById(id);
            _notificationService.SendOrderConfirmation(order);

            return RedirectToAction("Details", new { id });
        }

        [HttpPost]
        public IActionResult Process(int id)
        {
            _orderService.StartProcessingOrder(id);
            return RedirectToAction("Details", new { id });
        }

        [HttpPost]
        public IActionResult Ship(int id, string trackingNumber, string carrier, string warehouseCode)
        {
            _orderService.StartProcessingOrder(id);
            _orderService.ShipOrder(id, trackingNumber, carrier);

            // Legacy: sync HTTP call to shipping integration — blocks the request thread
            var order = _orderService.GetOrderById(id);
            var resolvedWarehouseCode = string.IsNullOrWhiteSpace(warehouseCode) ? "MAIN" : warehouseCode;
            var warehouseNotified = _shippingService.NotifyWarehouse(id, resolvedWarehouseCode);
            var result = _shippingService.RegisterShipmentWithCarrier(trackingNumber, carrier, order);

            if (!result)
                TempData["Warning"] = "Order shipped locally but carrier registration failed.";
            if (!warehouseNotified)
                TempData["Warning"] = "Order shipped, but warehouse notification failed.";

            _notificationService.SendShippingNotification(order);

            return RedirectToAction("Details", new { id });
        }
    }
}
