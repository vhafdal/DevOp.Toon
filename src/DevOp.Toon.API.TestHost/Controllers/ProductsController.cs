using Microsoft.AspNetCore.Mvc;
using DevOp.Toon.API.TestHost.Services;
using DevOp.Toon.Benchmarks.Models;

namespace DevOp.Toon.API.TestHost.Controllers;

[ApiController]
[Route("api/products")]
public sealed class ProductsController(ProductDataService productDataService) : ControllerBase
{
    [HttpGet("{page:int}/{pageSize:int}")]
    [ProducesResponseType(typeof(IReadOnlyList<Product>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<IReadOnlyList<Product>> Get(int page, int pageSize)
    {
        if (page < 1)
        {
            return ValidationProblem(detail: "The page number must be greater than or equal to 1.");
        }

        if (pageSize < 1)
        {
            return ValidationProblem(detail: "The page size must be greater than or equal to 1.");
        }

        if (!productDataService.TryGetPage(page, pageSize, out var products))
        {
            return NotFound(new ProblemDetails
            {
                Title = "Page not found",
                Detail = $"Page {page} with size {pageSize} is outside the available product range."
            });
        }

        return Ok(products);
    }
}
