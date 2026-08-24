public interface ISoftDelete
{
  bool IsActive { get; }
  void Deactivate();
}