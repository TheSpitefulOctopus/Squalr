﻿using Binarysharp.MemoryManagement;
using Binarysharp.MemoryManagement.Memory;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Anathema
{
    /// <summary>
    /// Defines a snapshot of memory in an external process.
    /// </summary>
    public class SnapshotRegion : RemoteRegion, IEnumerable
    {
        protected Byte[] CurrentValues;     // Most recently read values
        protected Byte[] PreviousValues;    // Previously read values
        protected Type ElementType;         // Element type for this
        protected BitArray Valid;           // Valid bits for use in filtering scans

        protected Int32 RegionExtension;    // Variable to indicate a safe number of read-over bytes

        public SnapshotRegion(IntPtr BaseAddress, Int32 RegionSize) : base(null, BaseAddress, RegionSize) { RegionExtension = 0; }
        public SnapshotRegion(RemoteRegion RemoteRegion) : base(null, RemoteRegion.BaseAddress, RemoteRegion.RegionSize) { RegionExtension = 0; }

        /// <summary>
        /// Indexer to access a unified snapshot element at the specified index
        /// </summary>
        /// <param name="Index"></param>
        /// <returns></returns>
        public SnapshotElement this[Int32 Index]
        {
            get
            {
                return new SnapshotElement(
                BaseAddress + Index, this, Index,
                ElementType,
                Valid == null ? false : Valid[Index],
                (CurrentValues == null || ElementType == null) ? (Byte[])null : CurrentValues.SubArray(Index, Marshal.SizeOf(ElementType)),
                (PreviousValues == null || ElementType == null) ? (Byte[])null : PreviousValues.SubArray(Index, Marshal.SizeOf(ElementType))
                );
            }
            set
            {
                if (this.Valid != null) Valid[Index] = value.Valid;
            }
        }

        public Byte[] ReadAllSnapshotMemory(MemorySharp MemoryEditor, Boolean KeepValues = true)
        {
            Boolean SuccessReading = false;
            Byte[] CurrentValues = MemoryEditor.ReadBytes(this.BaseAddress, this.RegionSize + RegionExtension, out SuccessReading, false);

            if (!SuccessReading)
                throw new ScanFailedException();

            if (KeepValues)
                SetCurrentValues(CurrentValues);

            return CurrentValues;
        }

        /// <summary>
        /// Returns all subregions of this region which are marked as valid. Will collect extended values based on the element type.
        /// </summary>
        /// <returns></returns>
        public List<SnapshotRegion> GetValidRegions()
        {
            List<SnapshotRegion> ValidRegions = new List<SnapshotRegion>();
            for (Int32 StartIndex = 0; StartIndex < Valid.Length; StartIndex++)
            {
                if (!Valid[StartIndex])
                    continue;

                // Determine length of this segment of valid regions
                Int32 ValidRegionSize = 0;
                while (StartIndex + (++ValidRegionSize) < Valid.Length && Valid[StartIndex + ValidRegionSize]) { }

                // Create the subregion from this segment
                SnapshotRegion SubRegion = new SnapshotRegion(this.BaseAddress + StartIndex, ValidRegionSize);
                if (CurrentValues != null)
                    SubRegion.SetCurrentValues(CurrentValues.LargestSubArray(StartIndex, ValidRegionSize + Marshal.SizeOf(ElementType)));
                SubRegion.SetElementType(ElementType);

                ValidRegions.Add(SubRegion);

                StartIndex += ValidRegionSize;
            }

            return ValidRegions;
        }

        public void ExpandRegion()
        {
            Int32 ExpandSize = Marshal.SizeOf(ElementType) - 1;
            this.RegionSize += ExpandSize; // TODO overflow checking
        }

        public void ShrinkRegion()
        {
            Int32 ShrinkSize = Marshal.SizeOf(ElementType) - 1;
            if (RegionSize >= ShrinkSize)
            {
                this.RegionSize -= ShrinkSize;
                RegionExtension = ShrinkSize;
            }
            else
            {
                RegionExtension = RegionSize;
                RegionSize = 0;
            }
        }

        public void MarkAllValid()
        {
            Valid = new BitArray(RegionSize, true);
        }

        public void MarkAllInvalid()
        {
            Valid = new BitArray(RegionSize, false);
        }

        public void SetElementType(Type ElementType)
        {
            this.ElementType = ElementType;
        }

        public void SetCurrentValues(Byte[] NewValues)
        {
            PreviousValues = CurrentValues;
            CurrentValues = NewValues;
        }

        public void SetPreviousValues(Byte[] NewValues)
        {
            PreviousValues = NewValues;
        }

        public Byte[] GetCurrentValues()
        {
            return CurrentValues;
        }

        public Byte[] GetPreviousValues()
        {
            return PreviousValues;
        }

        public Type GetElementType()
        {
            return ElementType;
        }

        /// <summary>
        /// Returns true if an region can be compared with itself: previous and current values are initialized
        /// </summary>
        /// <returns></returns>
        public Boolean CanCompare()
        {
            if (PreviousValues == null || CurrentValues == null || PreviousValues.Length != CurrentValues.Length)
                return false;
            return true;
        }

        public virtual IEnumerator GetEnumerator()
        {
            for (Int32 Index = 0; Index < RegionSize; Index++)
                yield return this[Index];
        }
    }

    /// <summary>
    /// Defines a snapshot of memory in an external process, as well as assigned labels to this memory.
    /// </summary>
    public class SnapshotRegion<LabelType> : SnapshotRegion where LabelType : struct
    {
        private LabelType?[] ElementLabels; // Labels for individual elements
        public SnapshotRegion(IntPtr BaseAddress, Int32 RegionSize) : base(BaseAddress, RegionSize) { }
        public SnapshotRegion(RemoteRegion RemoteRegion) : base(RemoteRegion) { }
        public SnapshotRegion(SnapshotRegion SnapshotRegion) : base(SnapshotRegion)
        {
            CurrentValues = SnapshotRegion.GetCurrentValues() == null ? null : (Byte[])SnapshotRegion.GetCurrentValues().Clone();
            PreviousValues = SnapshotRegion.GetPreviousValues() == null ? null : (Byte[])SnapshotRegion.GetPreviousValues().Clone();
        }

        public LabelType?[] GetElementLabels()
        {
            return ElementLabels;
        }

        public void SetElementLabels(LabelType? ElementLabel)
        {
            this.ElementLabels = Enumerable.Repeat(ElementLabel, RegionSize).ToArray();
        }

        public void SetElementLabels(LabelType?[] ElementLabels)
        {
            this.ElementLabels = ElementLabels;
        }

        /// <summary>
        /// Indexer to access a labeled unified snapshot element at the specified index
        /// </summary>
        /// <param name="Index"></param>
        /// <returns></returns>
        public new SnapshotElement<LabelType> this[Int32 Index]
        {
            get
            {
                return new SnapshotElement<LabelType>(
                BaseAddress + Index, this, Index,
                ElementType,
                Valid == null ? false : Valid[Index],
                (CurrentValues == null || ElementType == null) ? (Byte[])null : CurrentValues.SubArray(Index, Marshal.SizeOf(ElementType)),
                (PreviousValues == null || ElementType == null) ? (Byte[])null : PreviousValues.SubArray(Index, Marshal.SizeOf(ElementType)),
                ElementLabels == null ? (LabelType?)null : ElementLabels[Index]
                );
            }
            set
            {
                if (value.ElementType != null) ElementType = value.ElementType; else ElementType = null;
                if (this.Valid != null) Valid[Index] = value.Valid;
                if (value.MemoryLabel != null) ElementLabels[Index] = value.MemoryLabel.Value; else ElementLabels[Index] = null;
            }
        }

        public new List<SnapshotRegion<LabelType>> GetValidRegions()
        {
            List<SnapshotRegion<LabelType>> ValidRegions = new List<SnapshotRegion<LabelType>>();
            for (Int32 StartIndex = 0; StartIndex < Valid.Length; StartIndex++)
            {
                if (!Valid[StartIndex])
                    continue;

                // Get the length of this valid region
                Int32 ValidRegionSize = 0;
                while (StartIndex + (++ValidRegionSize) < Valid.Length && Valid[StartIndex + ValidRegionSize]) { }

                // Create new subregion from this valid region
                SnapshotRegion<LabelType> SubRegion = new SnapshotRegion<LabelType>(this.BaseAddress + StartIndex, ValidRegionSize);
                if (CurrentValues != null)
                    SubRegion.SetCurrentValues(CurrentValues.LargestSubArray(StartIndex, ValidRegionSize + Marshal.SizeOf(ElementType)));
                SubRegion.SetElementType(ElementType);
                if (ElementLabels != null)
                    SubRegion.SetElementLabels(ElementLabels.LargestSubArray(StartIndex, ValidRegionSize + Marshal.SizeOf(ElementType)));

                ValidRegions.Add(SubRegion);

                StartIndex += ValidRegionSize;
            }

            return ValidRegions;
        }

        public override IEnumerator GetEnumerator()
        {
            for (Int32 Index = 0; Index < RegionSize; Index++)
                yield return this[Index];
        }
    }
}