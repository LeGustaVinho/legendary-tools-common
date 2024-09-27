using System;
using NUnit.Framework;

namespace LegendaryTools.Tests.Utils
{
    [Flags]
    public enum TestFlags
    {
        None = 0,
        Flag1 = 1 << 0, // 1
        Flag2 = 1 << 1, // 2
        Flag3 = 1 << 2, // 4
        Flag4 = 1 << 3, // 8
        All = Flag1 | Flag2 | Flag3 | Flag4
    }
    
    public class FlagUtilTests
    {
        // ===========================
        // Generic Enum Tests
        // ===========================

        // Tests for Has<T>
        [Test]
        public void Has_Enum_HasSingleFlag_ReturnsTrue()
        {
            var flags = TestFlags.Flag1;
            Assert.IsTrue(FlagUtil.Has(flags, TestFlags.Flag1));
        }

        [Test]
        public void Has_Enum_DoesNotHaveFlag_ReturnsFalse()
        {
            var flags = TestFlags.Flag1;
            Assert.IsFalse(FlagUtil.Has(flags, TestFlags.Flag2));
        }

        [Test]
        public void Has_Enum_HasMultipleFlags_ReturnsTrue()
        {
            var flags = TestFlags.Flag1 | TestFlags.Flag2;
            Assert.IsTrue(FlagUtil.Has(flags, TestFlags.Flag1));
            Assert.IsTrue(FlagUtil.Has(flags, TestFlags.Flag2));
        }

        [Test]
        public void Has_Enum_HasNoFlags_ReturnsFalse()
        {
            var flags = TestFlags.None;
            Assert.IsFalse(FlagUtil.Has(flags, TestFlags.Flag1));
        }

        [Test]
        public void Has_Enum_HasAllFlags_ReturnsTrue()
        {
            var flags = TestFlags.All;
            Assert.IsTrue(FlagUtil.Has(flags, TestFlags.Flag3));
            Assert.IsTrue(FlagUtil.Has(flags, TestFlags.Flag4));
        }

        // Tests for Add<T>
        [Test]
        public void Add_Enum_AddSingleFlag()
        {
            var flags = TestFlags.Flag1;
            var result = FlagUtil.Add(flags, TestFlags.Flag2);
            Assert.AreEqual(TestFlags.Flag1 | TestFlags.Flag2, result);
        }

        [Test]
        public void Add_Enum_AddMultipleFlags()
        {
            var flags = TestFlags.Flag1;
            var result = FlagUtil.Add(flags, TestFlags.Flag2 | TestFlags.Flag3);
            Assert.AreEqual(TestFlags.Flag1 | TestFlags.Flag2 | TestFlags.Flag3, result);
        }

        [Test]
        public void Add_Enum_AddExistingFlag()
        {
            var flags = TestFlags.Flag1 | TestFlags.Flag2;
            var result = FlagUtil.Add(flags, TestFlags.Flag2);
            Assert.AreEqual(TestFlags.Flag1 | TestFlags.Flag2, result);
        }

        [Test]
        public void Add_Enum_AddNoFlags()
        {
            var flags = TestFlags.Flag1;
            var result = FlagUtil.Add(flags, TestFlags.None);
            Assert.AreEqual(TestFlags.Flag1, result);
        }

        [Test]
        public void Add_Enum_AddAllFlags()
        {
            var flags = TestFlags.None;
            var result = FlagUtil.Add(flags, TestFlags.All);
            Assert.AreEqual(TestFlags.All, result);
        }

        // Tests for Remove<T>
        [Test]
        public void Remove_Enum_RemoveSingleFlag()
        {
            var flags = TestFlags.Flag1 | TestFlags.Flag2;
            var result = FlagUtil.Remove(flags, TestFlags.Flag1);
            Assert.AreEqual(TestFlags.Flag2, result);
        }

        [Test]
        public void Remove_Enum_RemoveMultipleFlags()
        {
            var flags = TestFlags.Flag1 | TestFlags.Flag2 | TestFlags.Flag3;
            var result = FlagUtil.Remove(flags, TestFlags.Flag1 | TestFlags.Flag3);
            Assert.AreEqual(TestFlags.Flag2, result);
        }

        [Test]
        public void Remove_Enum_RemoveNonExistingFlag()
        {
            var flags = TestFlags.Flag1;
            var result = FlagUtil.Remove(flags, TestFlags.Flag2);
            Assert.AreEqual(TestFlags.Flag1, result);
        }

        [Test]
        public void Remove_Enum_RemoveAllFlags()
        {
            var flags = TestFlags.All;
            var result = FlagUtil.Remove(flags, TestFlags.All);
            Assert.AreEqual(TestFlags.None, result);
        }

        [Test]
        public void Remove_Enum_RemoveNoFlags()
        {
            var flags = TestFlags.Flag1;
            var result = FlagUtil.Remove(flags, TestFlags.None);
            Assert.AreEqual(TestFlags.Flag1, result);
        }

        // Tests for Is<T>
        [Test]
        public void Is_Enum_IsSameFlag_ReturnsTrue()
        {
            var flag = TestFlags.Flag1;
            Assert.IsTrue(FlagUtil.Is(flag, TestFlags.Flag1));
        }

        [Test]
        public void Is_Enum_IsDifferentFlag_ReturnsFalse()
        {
            var flag = TestFlags.Flag1;
            Assert.IsFalse(FlagUtil.Is(flag, TestFlags.Flag2));
        }

        [Test]
        public void Is_Enum_IsCombinedFlags_ReturnsFalse()
        {
            var flag = TestFlags.Flag1 | TestFlags.Flag2;
            Assert.IsFalse(FlagUtil.Is(flag, TestFlags.Flag1));
        }

        [Test]
        public void Is_Enum_IsNoneFlag_ReturnsTrue()
        {
            var flag = TestFlags.None;
            Assert.IsTrue(FlagUtil.Is(flag, TestFlags.None));
        }

        [Test]
        public void Is_Enum_IsAllFlags_ReturnsTrue()
        {
            var flag = TestFlags.All;
            Assert.IsTrue(FlagUtil.Is(flag, TestFlags.All));
        }

        // ===========================
        // Int Tests
        // ===========================

        // Tests for Has(int, int)
        [Test]
        public void Has_Int_HasSingleFlag_ReturnsTrue()
        {
            int flags = 1; // 0001
            int flag = 1;
            Assert.IsTrue(FlagUtil.Has(flags, flag));
        }

        [Test]
        public void Has_Int_DoesNotHaveFlag_ReturnsFalse()
        {
            int flags = 1; // 0001
            int flag = 2;
            Assert.IsFalse(FlagUtil.Has(flags, flag));
        }

        [Test]
        public void Has_Int_HasMultipleFlags_ReturnsTrue()
        {
            int flags = 3; // 0011
            Assert.IsTrue(FlagUtil.Has(flags, 1));
            Assert.IsTrue(FlagUtil.Has(flags, 2));
        }

        [Test]
        public void Has_Int_HasNoFlags_ReturnsFalse()
        {
            int flags = 0;
            Assert.IsFalse(FlagUtil.Has(flags, 1));
        }

        [Test]
        public void Has_Int_HasAllFlags_ReturnsTrue()
        {
            int flags = 15; // 1111
            Assert.IsTrue(FlagUtil.Has(flags, 8));
            Assert.IsTrue(FlagUtil.Has(flags, 4));
        }

        // Tests for Add(int, int)
        [Test]
        public void Add_Int_AddSingleFlag()
        {
            int flags = 1;
            int flagToAdd = 2;
            int result = FlagUtil.Add(flags, flagToAdd);
            Assert.AreEqual(3, result);
        }

        [Test]
        public void Add_Int_AddMultipleFlags()
        {
            int flags = 1;
            int flagToAdd = 6; // 0110
            int result = FlagUtil.Add(flags, flagToAdd);
            Assert.AreEqual(7, result);
        }

        [Test]
        public void Add_Int_AddExistingFlag()
        {
            int flags = 3; // 0011
            int flagToAdd = 2;
            int result = FlagUtil.Add(flags, flagToAdd);
            Assert.AreEqual(3, result);
        }

        [Test]
        public void Add_Int_AddNoFlags()
        {
            int flags = 1;
            int flagToAdd = 0;
            int result = FlagUtil.Add(flags, flagToAdd);
            Assert.AreEqual(1, result);
        }

        [Test]
        public void Add_Int_AddAllFlags()
        {
            int flags = 0;
            int flagToAdd = 15; // 1111
            int result = FlagUtil.Add(flags, flagToAdd);
            Assert.AreEqual(15, result);
        }

        // Tests for Remove(int, int)
        [Test]
        public void Remove_Int_RemoveSingleFlag()
        {
            int flags = 3; // 0011
            int flagToRemove = 1;
            int result = FlagUtil.Remove(flags, flagToRemove);
            Assert.AreEqual(2, result);
        }

        [Test]
        public void Remove_Int_RemoveMultipleFlags()
        {
            int flags = 7; // 0111
            int flagToRemove = 5; // 0101
            int result = FlagUtil.Remove(flags, flagToRemove);
            Assert.AreEqual(2, result);
        }

        [Test]
        public void Remove_Int_RemoveNonExistingFlag()
        {
            int flags = 1;
            int flagToRemove = 2;
            int result = FlagUtil.Remove(flags, flagToRemove);
            Assert.AreEqual(1, result);
        }

        [Test]
        public void Remove_Int_RemoveAllFlags()
        {
            int flags = 15; // 1111
            int flagToRemove = 15;
            int result = FlagUtil.Remove(flags, flagToRemove);
            Assert.AreEqual(0, result);
        }

        [Test]
        public void Remove_Int_RemoveNoFlags()
        {
            int flags = 1;
            int flagToRemove = 0;
            int result = FlagUtil.Remove(flags, flagToRemove);
            Assert.AreEqual(1, result);
        }

        // Tests for Is(int, int)
        [Test]
        public void Is_Int_IsSameFlag_ReturnsTrue()
        {
            int flag1 = 1;
            int flag2 = 1;
            Assert.IsTrue(FlagUtil.Is(flag1, flag2));
        }

        [Test]
        public void Is_Int_IsDifferentFlag_ReturnsFalse()
        {
            int flag1 = 1;
            int flag2 = 2;
            Assert.IsFalse(FlagUtil.Is(flag1, flag2));
        }

        [Test]
        public void Is_Int_IsCombinedFlags_ReturnsFalse()
        {
            int flag1 = 3; // 0011
            int flag2 = 1;
            Assert.IsFalse(FlagUtil.Is(flag1, flag2));
        }

        [Test]
        public void Is_Int_IsNoneFlag_ReturnsTrue()
        {
            int flag1 = 0;
            int flag2 = 0;
            Assert.IsTrue(FlagUtil.Is(flag1, flag2));
        }

        [Test]
        public void Is_Int_IsAllFlags_ReturnsTrue()
        {
            int flag1 = 15;
            int flag2 = 15;
            Assert.IsTrue(FlagUtil.Is(flag1, flag2));
        }

        // ===========================
        // Long Tests
        // ===========================

        // Tests for Has(long, long)
        [Test]
        public void Has_Long_HasSingleFlag_ReturnsTrue()
        {
            long flags = 1L;
            long flag = 1L;
            Assert.IsTrue(FlagUtil.Has(flags, flag));
        }

        [Test]
        public void Has_Long_DoesNotHaveFlag_ReturnsFalse()
        {
            long flags = 1L;
            long flag = 2L;
            Assert.IsFalse(FlagUtil.Has(flags, flag));
        }

        [Test]
        public void Has_Long_HasMultipleFlags_ReturnsTrue()
        {
            long flags = 3L;
            Assert.IsTrue(FlagUtil.Has(flags, 1L));
            Assert.IsTrue(FlagUtil.Has(flags, 2L));
        }

        [Test]
        public void Has_Long_HasNoFlags_ReturnsFalse()
        {
            long flags = 0L;
            Assert.IsFalse(FlagUtil.Has(flags, 1L));
        }

        [Test]
        public void Has_Long_HasAllFlags_ReturnsTrue()
        {
            long flags = 15L;
            Assert.IsTrue(FlagUtil.Has(flags, 8L));
            Assert.IsTrue(FlagUtil.Has(flags, 4L));
        }

        // Tests for Add(long, long)
        [Test]
        public void Add_Long_AddSingleFlag()
        {
            long flags = 1L;
            long flagToAdd = 2L;
            long result = FlagUtil.Add(flags, flagToAdd);
            Assert.AreEqual(3L, result);
        }

        [Test]
        public void Add_Long_AddMultipleFlags()
        {
            long flags = 1L;
            long flagToAdd = 6L;
            long result = FlagUtil.Add(flags, flagToAdd);
            Assert.AreEqual(7L, result);
        }

        [Test]
        public void Add_Long_AddExistingFlag()
        {
            long flags = 3L;
            long flagToAdd = 2L;
            long result = FlagUtil.Add(flags, flagToAdd);
            Assert.AreEqual(3L, result);
        }

        [Test]
        public void Add_Long_AddNoFlags()
        {
            long flags = 1L;
            long flagToAdd = 0L;
            long result = FlagUtil.Add(flags, flagToAdd);
            Assert.AreEqual(1L, result);
        }

        [Test]
        public void Add_Long_AddAllFlags()
        {
            long flags = 0L;
            long flagToAdd = 15L;
            long result = FlagUtil.Add(flags, flagToAdd);
            Assert.AreEqual(15L, result);
        }

        // Tests for Remove(long, long)
        [Test]
        public void Remove_Long_RemoveSingleFlag()
        {
            long flags = 3L;
            long flagToRemove = 1L;
            long result = FlagUtil.Remove(flags, flagToRemove);
            Assert.AreEqual(2L, result);
        }

        [Test]
        public void Remove_Long_RemoveMultipleFlags()
        {
            long flags = 7L;
            long flagToRemove = 5L;
            long result = FlagUtil.Remove(flags, flagToRemove);
            Assert.AreEqual(2L, result);
        }

        [Test]
        public void Remove_Long_RemoveNonExistingFlag()
        {
            long flags = 1L;
            long flagToRemove = 2L;
            long result = FlagUtil.Remove(flags, flagToRemove);
            Assert.AreEqual(1L, result);
        }

        [Test]
        public void Remove_Long_RemoveAllFlags()
        {
            long flags = 15L;
            long flagToRemove = 15L;
            long result = FlagUtil.Remove(flags, flagToRemove);
            Assert.AreEqual(0L, result);
        }

        [Test]
        public void Remove_Long_RemoveNoFlags()
        {
            long flags = 1L;
            long flagToRemove = 0L;
            long result = FlagUtil.Remove(flags, flagToRemove);
            Assert.AreEqual(1L, result);
        }

        // Tests for Is(long, long)
        [Test]
        public void Is_Long_IsSameFlag_ReturnsTrue()
        {
            long flag1 = 1L;
            long flag2 = 1L;
            Assert.IsTrue(FlagUtil.Is(flag1, flag2));
        }

        [Test]
        public void Is_Long_IsDifferentFlag_ReturnsFalse()
        {
            long flag1 = 1L;
            long flag2 = 2L;
            Assert.IsFalse(FlagUtil.Is(flag1, flag2));
        }

        [Test]
        public void Is_Long_IsCombinedFlags_ReturnsFalse()
        {
            long flag1 = 3L;
            long flag2 = 1L;
            Assert.IsFalse(FlagUtil.Is(flag1, flag2));
        }

        [Test]
        public void Is_Long_IsNoneFlag_ReturnsTrue()
        {
            long flag1 = 0L;
            long flag2 = 0L;
            Assert.IsTrue(FlagUtil.Is(flag1, flag2));
        }

        [Test]
        public void Is_Long_IsAllFlags_ReturnsTrue()
        {
            long flag1 = 15L;
            long flag2 = 15L;
            Assert.IsTrue(FlagUtil.Is(flag1, flag2));
        }

        // ===========================
        // Float Tests
        // ===========================

        // Tests for Has(float, float)
        [Test]
        public void Has_Float_HasSingleFlag_ReturnsTrue()
        {
            float flags = 1f;
            float flag = 1f;
            Assert.IsTrue(FlagUtil.Has(flags, flag));
        }

        [Test]
        public void Has_Float_DoesNotHaveFlag_ReturnsFalse()
        {
            float flags = 1f;
            float flag = 2f;
            Assert.IsFalse(FlagUtil.Has(flags, flag));
        }

        [Test]
        public void Has_Float_HasMultipleFlags_ReturnsTrue()
        {
            float flags = 3f;
            Assert.IsTrue(FlagUtil.Has(flags, 1f));
            Assert.IsTrue(FlagUtil.Has(flags, 2f));
        }

        [Test]
        public void Has_Float_HasNoFlags_ReturnsFalse()
        {
            float flags = 0f;
            Assert.IsFalse(FlagUtil.Has(flags, 1f));
        }

        [Test]
        public void Has_Float_HasAllFlags_ReturnsTrue()
        {
            float flags = 15f;
            Assert.IsTrue(FlagUtil.Has(flags, 8f));
            Assert.IsTrue(FlagUtil.Has(flags, 4f));
        }

        // Tests for Add(float, float)
        [Test]
        public void Add_Float_AddSingleFlag()
        {
            float flags = 1f;
            float flagToAdd = 2f;
            long result = FlagUtil.Add(flags, flagToAdd);
            Assert.AreEqual(3L, result);
        }

        [Test]
        public void Add_Float_AddMultipleFlags()
        {
            float flags = 1f;
            float flagToAdd = 6f;
            long result = FlagUtil.Add(flags, flagToAdd);
            Assert.AreEqual(7L, result);
        }

        [Test]
        public void Add_Float_AddExistingFlag()
        {
            float flags = 3f;
            float flagToAdd = 2f;
            long result = FlagUtil.Add(flags, flagToAdd);
            Assert.AreEqual(3L, result);
        }

        [Test]
        public void Add_Float_AddNoFlags()
        {
            float flags = 1f;
            float flagToAdd = 0f;
            long result = FlagUtil.Add(flags, flagToAdd);
            Assert.AreEqual(1L, result);
        }

        [Test]
        public void Add_Float_AddAllFlags()
        {
            float flags = 0f;
            float flagToAdd = 15f;
            long result = FlagUtil.Add(flags, flagToAdd);
            Assert.AreEqual(15L, result);
        }

        // Tests for Remove(float, float)
        [Test]
        public void Remove_Float_RemoveSingleFlag()
        {
            float flags = 3f;
            float flagToRemove = 1f;
            long result = FlagUtil.Remove(flags, flagToRemove);
            Assert.AreEqual(2L, result);
        }

        [Test]
        public void Remove_Float_RemoveMultipleFlags()
        {
            float flags = 7f;
            float flagToRemove = 5f;
            long result = FlagUtil.Remove(flags, flagToRemove);
            Assert.AreEqual(2L, result);
        }

        [Test]
        public void Remove_Float_RemoveNonExistingFlag()
        {
            float flags = 1f;
            float flagToRemove = 2f;
            long result = FlagUtil.Remove(flags, flagToRemove);
            Assert.AreEqual(1L, result);
        }

        [Test]
        public void Remove_Float_RemoveAllFlags()
        {
            float flags = 15f;
            float flagToRemove = 15f;
            long result = FlagUtil.Remove(flags, flagToRemove);
            Assert.AreEqual(0L, result);
        }

        [Test]
        public void Remove_Float_RemoveNoFlags()
        {
            float flags = 1f;
            float flagToRemove = 0f;
            long result = FlagUtil.Remove(flags, flagToRemove);
            Assert.AreEqual(1L, result);
        }

        // Tests for Is(float, float)
        [Test]
        public void Is_Float_IsSameFlag_ReturnsTrue()
        {
            float flag1 = 1f;
            float flag2 = 1f;
            Assert.IsTrue(FlagUtil.Is(flag1, flag2));
        }

        [Test]
        public void Is_Float_IsDifferentFlag_ReturnsFalse()
        {
            float flag1 = 1f;
            float flag2 = 2f;
            Assert.IsFalse(FlagUtil.Is(flag1, flag2));
        }

        [Test]
        public void Is_Float_IsCombinedFlags_ReturnsFalse()
        {
            float flag1 = 3f;
            float flag2 = 1f;
            Assert.IsFalse(FlagUtil.Is(flag1, flag2));
        }

        [Test]
        public void Is_Float_IsNoneFlag_ReturnsTrue()
        {
            float flag1 = 0f;
            float flag2 = 0f;
            Assert.IsTrue(FlagUtil.Is(flag1, flag2));
        }

        [Test]
        public void Is_Float_IsAllFlags_ReturnsTrue()
        {
            float flag1 = 15f;
            float flag2 = 15f;
            Assert.IsTrue(FlagUtil.Is(flag1, flag2));
        }
    }
}