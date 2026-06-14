using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace LegacyOrderMgmt.Core.Tests
{
    public class RouteWiringTests
    {
        [Fact]
        public void SalesCsv_Action_UsesExpectedAttributeRouteTemplate()
        {
            var controllerSource = ReadRepoFile(@"LegacyOrderMgmt.Web\Controllers\InvoiceController.cs");

            Assert.Matches(
                new Regex(@"\[HttpGet\(""reports/sales-csv""\)\]\s*public IActionResult SalesCsv\(DateTime\? from, DateTime\? to\)", RegexOptions.Multiline),
                controllerSource);
        }

        [Fact]
        public void Startup_RegistersMvc_And_MapsDefaultRoute()
        {
            var startupSource = ReadRepoFile(@"LegacyOrderMgmt.Web\Startup.cs");

            Assert.Contains("services.AddMvc();", startupSource);
            Assert.Contains("app.UseMvc(routes =>", startupSource);
            Assert.Contains("template: \"{controller=Home}/{action=Index}/{id?}\"", startupSource);
        }

        private static string ReadRepoFile(string relativePathFromRepoRoot)
        {
            var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            var fullPath = Path.Combine(repoRoot, relativePathFromRepoRoot);
            return File.ReadAllText(fullPath);
        }
    }
}
