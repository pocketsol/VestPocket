using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TrieHard.PrefixLookup;

namespace VestPocket
{
    internal class UpdateTransaction: Transaction
    {
        private Kvp entity;
        private readonly object basedOn;

        public Kvp Record { get => entity; internal set => entity = value; }

        public UpdateTransaction(Kvp entity, object basedOn, bool throwOnError) : base(throwOnError)
        {
            this.entity = entity;
            this.basedOn = basedOn;
        }

        public override bool Validate(object existingEntity)
        {
            var matches = MatchesExisting(existingEntity);
            if (!matches)
            {
                entity = new Kvp(entity.Key, existingEntity);
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

        private bool MatchesExisting(object existingEntity)
        {
            if (basedOn is null && existingEntity is null)
            {
                return true;
            }
            //if (basedOn is IEquatable equatable)
            //{
            //    return equatable.Equals(existingEntity);
            //}
            return existingEntity.Equals(basedOn);
        }

        public override int Count => 1;

        public override Kvp this[int index]
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
