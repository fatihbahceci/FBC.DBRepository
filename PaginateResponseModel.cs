namespace FBC.DBRepository;

public class PaginateResponseModel<T>
{
    public PaginateResponseModel()
    {
        Items = Array.Empty<T>();
    }

    public int ItemsPerPage { get; set; }

    public int PageIndex { get; set; }

    public int TotalFilteredCount { get; set; }

    public int TotalPages { get; set; }

    public IList<T> Items { get; set; }

    public bool HasPrevius => PageIndex > 0;
    public bool HasNext => PageIndex + 1 < TotalPages;
}

