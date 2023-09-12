using System.Linq.Expressions;

namespace TransferERP.QueryLang;

public interface IQueryLang<T>
{
	public Expression<Func<T, bool>> Parse(string query);
	public bool TryParse(string query, out Expression<Func<T, bool>> result);
}
