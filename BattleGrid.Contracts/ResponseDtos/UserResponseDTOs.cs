namespace BattleGrid.Contracts.ResponseDtos
{
    public class UserResponseDto
    {
        public int UserID { get; set; }

        public string UserName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public bool IsAdmin { get; set; }

        public bool IsBanned { get; set; }

        public bool IsActive { get; set; }
    }

    public class PagedUsersResponseDto
    {
        public List<UserResponseDto> Items { get; set; } = [];

        public int Page { get; set; }

        public int PageSize { get; set; }

        public int TotalCount { get; set; }

        public int TotalPages { get; set; }
    }
}