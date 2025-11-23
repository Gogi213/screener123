using Microsoft.AspNetCore.Mvc;

namespace SpreadAggregator.Presentation.Controllers;

public class HomeController : Controller
{
    [HttpGet("/")]
    public IActionResult Index()
    {
        // Redirect to the modern Trade Screener Pro (screener.html)
        return Redirect("/screener.html");
    }
}
