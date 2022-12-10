using System.IO;

namespace VestPocket;

public interface ISerializer<T> where T : class, IEntity
{
    void Serialize(T entity, Stream stream);
    T Deserialize(byte[] data);
}