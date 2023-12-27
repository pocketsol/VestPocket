using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TrieHard.PrefixLookup;

namespace VestPocket
{
    internal class UpdateTransaction<T> : Transaction<T> where T : IEntity
    {
        private T entity;
        private readonly T basedOn;

        public T Entity { get => entity; internal set => entity = value; }

        public UpdateTransaction(T entity, T basedOn, bool throwOnError) : base(throwOnError)
        {
            this.entity = entity;
            this.basedOn = basedOn;
        }

        public override bool Validate(T existingEntity)
        {
            var matches = MatchesExisting(existingEntity);
            if (!matches)
            {
                entity = existingEntity;
                if (ThrowOnError)
                {
                    SetError(new ConcurrencyException(entity.Key));
                }
                else
                {
                    Complete();
                }
            }
            return matches;
        }

        private bool MatchesExisting(T existingEntity)
        {
            if (basedOn is null && existingEntity is null)
            {
                return true;
            }
            if (basedOn is IEquatable<T> equatable)
            {
                return equatable.Equals(existingEntity);
            }
            return existingEntity.Equals(basedOn);
        }

        public override int Count => 1;

        public override T this[int index]
        {
            get
            {
                if (index != 0) throw new IndexOutOfRangeException();
                return entity;
            }
            set
            {
                if (index != 0) throw new IndexOutOfRangeException();
                entity = value;
            }
        }
    }
}
