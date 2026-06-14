using LegacyOrderMgmt.Core.Data;
using LegacyOrderMgmt.Core.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;

namespace LegacyOrderMgmt.Core.Services
{
    public class OrderService
    {
        private readonly OrderDbContext _db;

        // Legacy: ReaderWriterLock instead of ReaderWriterLockSlim
        private static readonly ReaderWriterLock _cacheLock = new ReaderWriterLock();
        private static readonly Dictionary<int, byte[]> _orderCache = new Dictionary<int, byte[]>();

        public OrderService(OrderDbContext db)
        {
            _db = db;
        }

        // Legacy: synchronous EF Core queries — no async/await
        public List<Order> GetOrdersByCustomer(int customerId)
        {
            // Legacy: no AsNoTracking() on read-only query — unnecessary change tracking overhead
            return _db.Orders
                .Include(o => o.Lines)
                .Include(o => o.Customer)
                .Where(o => o.CustomerId == customerId)
                .ToList();
        }

        public Order GetOrderById(int orderId)
        {
            return _db.Orders
                .Include(o => o.Lines).ThenInclude(l => l.Product)
                .Include(o => o.Customer)
                .Include(o => o.Invoices)
                .Include(o => o.Shipments)
                .FirstOrDefault(o => o.Id == orderId);
        }

        public List<Order> GetPendingOrders()
        {
            // Legacy: magic number status comparison instead of enum
            return _db.Orders
                .Include(o => o.Customer)
                .Where(o => o.Status == 1 || o.Status == 2)
                .ToList();
        }

        // Legacy: DateTime.Now instead of DateTimeOffset.UtcNow
        public Order CreateOrder(int customerId, string createdBy)
        {
            var order = new Order
            {
                CustomerId = customerId,
                OrderNumber = GenerateOrderNumber(),
                OrderDate = DateTime.Now,
                Status = 0,
                CreatedBy = createdBy,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            _db.Orders.Add(order);
            _db.SaveChanges();
            return order;
        }

        public void ConfirmOrder(int orderId)
        {
            var order = _db.Orders.Find(orderId);
            if (order == null) return;

            order.Status = 1;
            order.UpdatedAt = DateTime.Now;

            // Legacy: raw SQL string interpolation — SQL injection risk
            var sql = $"UPDATE Orders SET LastConfirmedBy = '{System.Threading.Thread.CurrentPrincipal?.Identity?.Name}' WHERE Id = {orderId}";
            _db.Database.ExecuteSqlCommand(sql);

            _db.SaveChanges();
            CacheOrder(order);
        }

        public void StartProcessingOrder(int orderId)
        {
            var order = _db.Orders.Find(orderId);
            if (order == null) return;
            if (order.Status == 3 || order.Status == 4 || order.Status == 5) return;

            order.Status = 2;
            order.UpdatedAt = DateTime.Now;
            _db.SaveChanges();
        }

        public void ShipOrder(int orderId, string trackingNumber, string carrier)
        {
            var order = _db.Orders
                .Include(o => o.Shipments)
                .FirstOrDefault(o => o.Id == orderId);

            if (order == null) return;
            if (order.Status < 2)
            {
                order.Status = 2;
                order.UpdatedAt = DateTime.Now;
                _db.SaveChanges();
            }

            var shipment = new Shipment
            {
                OrderId = orderId,
                TrackingNumber = trackingNumber,
                Carrier = carrier,
                ShipDate = DateTime.Now,
                EstimatedDelivery = DateTime.Now.AddDays(3),
                ShipmentStatus = 1,
                CreatedAt = DateTime.Now
            };

            order.Shipments.Add(shipment);
            order.Status = 3;
            order.ShippedDate = DateTime.Now;
            order.UpdatedAt = DateTime.Now;

            _db.SaveChanges();
        }

        public List<Order> GetOrdersForDateRange(DateTime from, DateTime to)
        {
            // Legacy: raw SQL string concatenation for date range query
            var fromStr = from.ToString("yyyy-MM-dd");
            var toStr = to.ToString("yyyy-MM-dd");
            var sql = "SELECT * FROM Orders WHERE OrderDate >= '" + fromStr + "' AND OrderDate <= '" + toStr + "'";

            // Legacy: FromSql with raw concatenated string — not parameterized
            return _db.Orders.FromSql(sql).ToList();
        }

        // Legacy: BinaryFormatter for in-memory order caching — unsafe and removed in .NET 7+
        private void CacheOrder(Order order)
        {
            _cacheLock.AcquireWriterLock(5000);
            try
            {
                using (var ms = new MemoryStream())
                {
                    var formatter = new BinaryFormatter();
#pragma warning disable SYSLIB0011
                    formatter.Serialize(ms, order);
#pragma warning restore SYSLIB0011
                    _orderCache[order.Id] = ms.ToArray();
                }
            }
            finally
            {
                _cacheLock.ReleaseWriterLock();
            }
        }

        public Order GetCachedOrder(int orderId)
        {
            _cacheLock.AcquireReaderLock(5000);
            try
            {
                if (!_orderCache.TryGetValue(orderId, out var bytes))
                    return null;

                using (var ms = new MemoryStream(bytes))
                {
                    var formatter = new BinaryFormatter();
#pragma warning disable SYSLIB0011
                    return (Order)formatter.Deserialize(ms);
#pragma warning restore SYSLIB0011
                }
            }
            finally
            {
                _cacheLock.ReleaseReaderLock();
            }
        }

        // Legacy: non-sequential order number generation without atomicity guarantees
        private string GenerateOrderNumber()
        {
            var lastOrder = _db.Orders.OrderByDescending(o => o.Id).FirstOrDefault();
            var nextId = (lastOrder?.Id ?? 0) + 1;
            return $"ORD-{DateTime.Now.Year}-{nextId:D5}";
        }
    }
}
