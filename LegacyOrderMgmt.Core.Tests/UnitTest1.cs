using LegacyOrderMgmt.Core.Data;
using LegacyOrderMgmt.Core.Models;
using LegacyOrderMgmt.Core.Services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace LegacyOrderMgmt.Core.Tests
{
    public class PricingEngineTests
    {
        [Fact]
        public void CalculateLineTotal_AppliesPercentDiscount()
        {
            using (var db = CreateDbContext())
            {
                ResetPricingCache();
                var engine = new PricingEngine(db);

                var total = engine.CalculateLineTotal(2, 50m, 10m);

                Assert.Equal(90m, total);
            }
        }

        [Fact]
        public void CalculateDiscount_ReturnsHighestApplicableRule()
        {
            using (var db = CreateDbContext())
            {
                ResetPricingCache();
                db.PricingRules.AddRange(
                    new PricingRule
                    {
                        CustomerTier = "Gold",
                        DiscountPercent = 5m,
                        IsActive = true,
                        ValidFrom = DateTime.Now.AddDays(-1),
                        ValidTo = DateTime.Now.AddDays(1),
                        MinOrderValue = 100m
                    },
                    new PricingRule
                    {
                        CustomerTier = "Gold",
                        DiscountPercent = 8m,
                        IsActive = true,
                        ValidFrom = DateTime.Now.AddDays(-1),
                        ValidTo = DateTime.Now.AddDays(1),
                        MinOrderValue = 100m
                    });
                db.SaveChanges();

                var engine = new PricingEngine(db);
                var order = new Order { SubTotal = 150m };

                var discount = engine.CalculateDiscount(order, "Gold");

                Assert.Equal(8m, discount);
            }
        }

        private static void ResetPricingCache()
        {
            var cacheField = typeof(PricingEngine).GetField("_cachedRules", BindingFlags.Static | BindingFlags.NonPublic);
            var loadedAtField = typeof(PricingEngine).GetField("_cacheLoadedAt", BindingFlags.Static | BindingFlags.NonPublic);
            cacheField?.SetValue(null, null);
            loadedAtField?.SetValue(null, DateTime.MinValue);
        }

        private static OrderDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<OrderDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new OrderDbContext(options);
        }
    }

    public class OrderServiceTests
    {
        [Fact]
        public void StartProcessingOrder_ChangesDraftOrderToProcessing()
        {
            using (var db = CreateDbContext())
            {
                db.Orders.Add(new Order { CustomerId = 101, Status = 0 });
                db.SaveChanges();

                var service = new OrderService(db);
                var orderId = db.Orders.Single().Id;

                service.StartProcessingOrder(orderId);

                Assert.Equal(2, db.Orders.Single().Status);
            }
        }

        private static OrderDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<OrderDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new OrderDbContext(options);
        }
    }
}
