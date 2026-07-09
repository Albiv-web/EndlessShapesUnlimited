using BrilliantSkies.Blocks.Decorative;
using BrilliantSkies.Ftd.Constructs.Modules.All.Decorations;
using DecoLimitLifter.DecorationEditMode;
using System;
using UnityEngine;

namespace EndlessShapes2.Polygon
{
    public class MimicAndDecorationCommonData
    {
        private enum MAD_DataType
        {
            None,
            Mimic,
            Decoration
        }

        private class MDCD
        {
            public Guid MeshGuid { get; set; } = Guid.Empty;

            public Vector3 Positioning { get; set; } = Vector3.zero;

            public Vector3 Scaling { get; set; } = Vector3.one;

            public Vector3 Orientation { get; set; } = Vector3.zero;

            public int ColorIndex { get; set; } = 0;

            public Guid MaterialReplacement { get; set; } = Guid.Empty;
        }

        public static void Copy(MimicAndDecorationCommonData source, MimicAndDecorationCommonData destination)
        {
            destination.MeshGuid = source.MeshGuid;
            destination.Positioning = source.Positioning;
            destination.Scaling = source.Scaling;
            destination.Orientation = source.Orientation;
            destination.ColorIndex = source.ColorIndex;
            destination.MaterialReplacement = source.MaterialReplacement;
        }



        private MAD_DataType _dataType = MAD_DataType.None;

        private object _obj = null;

        internal bool TrySetStandaloneData(
            Guid meshGuid,
            Vector3 positioning,
            Vector3 scaling,
            Vector3 orientation,
            int colorIndex)
        {
            if (_dataType != MAD_DataType.None)
                return false;

            var data = (MDCD)_obj;
            data.MeshGuid = meshGuid;
            data.Positioning = positioning;
            data.Scaling = scaling;
            data.Orientation = orientation;
            if (colorIndex >= 0)
                data.ColorIndex = colorIndex;
            return true;
        }

        internal bool TryGetStandaloneData(
            out Guid meshGuid,
            out Vector3 positioning,
            out Vector3 scaling,
            out Vector3 orientation,
            out int colorIndex)
        {
            meshGuid = Guid.Empty;
            positioning = Vector3.zero;
            scaling = Vector3.one;
            orientation = Vector3.zero;
            colorIndex = 0;
            if (_dataType != MAD_DataType.None)
                return false;

            var data = (MDCD)_obj;
            meshGuid = data.MeshGuid;
            positioning = data.Positioning;
            scaling = data.Scaling;
            orientation = data.Orientation;
            colorIndex = data.ColorIndex;
            return true;
        }

        public Guid MeshGuid
        {
            get
            {
                switch (_dataType)
                {
                    case MAD_DataType.None:
                        return ((MDCD)_obj).MeshGuid;
                    case MAD_DataType.Mimic:
                        return ((Mimic)_obj).Data.MeshGuid.Us;
                    case MAD_DataType.Decoration:
                        return ((Decoration)_obj).MeshGuid.Us;
                    default:
                        return default;
                }
            }
            set
            {
                switch (_dataType)
                {
                    case MAD_DataType.None:
                        ((MDCD)_obj).MeshGuid = value;
                        break;
                    case MAD_DataType.Mimic:
                        ((Mimic)_obj).Data.MeshGuid.Us = value;
                        break;
                    case MAD_DataType.Decoration:
                        ((Decoration)_obj).MeshGuid.Us = value;
                        break;
                }
            }
        }

        public Vector3 Positioning
        {
            get
            {
                switch (_dataType)
                {
                    case MAD_DataType.None:
                        return ((MDCD)_obj).Positioning;
                    case MAD_DataType.Mimic:
                        return ((Mimic)_obj).Data.Positioning.Us;
                    case MAD_DataType.Decoration:
                        return ((Decoration)_obj).Positioning.Us;
                    default:
                        return default;
                }
            }
            set
            {
                switch (_dataType)
                {
                    case MAD_DataType.None:
                        ((MDCD)_obj).Positioning = value;
                        break;
                    case MAD_DataType.Mimic:
                        ((Mimic)_obj).Data.Positioning.Us = value;
                        break;
                    case MAD_DataType.Decoration:
                        ((Decoration)_obj).Positioning.Us = value;
                        break;
                }
            }
        }

        public Vector3 Scaling
        {
            get
            {
                switch (_dataType)
                {
                    case MAD_DataType.None:
                        return ((MDCD)_obj).Scaling;
                    case MAD_DataType.Mimic:
                        return ((Mimic)_obj).Data.Scaling.Us;
                    case MAD_DataType.Decoration:
                        return ((Decoration)_obj).Scaling.Us;
                    default:
                        return default;
                }
            }
            set
            {
                switch (_dataType)
                {
                    case MAD_DataType.None:
                        ((MDCD)_obj).Scaling = value;
                        break;
                    case MAD_DataType.Mimic:
                        ((Mimic)_obj).Data.Scaling.Us = value;
                        break;
                    case MAD_DataType.Decoration:
                        Decoration decoration = (Decoration)_obj;
                        DecorationScaleBounds.AllowExtendedScale(decoration);
                        decoration.Scaling.Us = value;
                        break;
                }
            }
        }

        public Vector3 Orientation
        {
            get
            {
                switch (_dataType)
                {
                    case MAD_DataType.None:
                        return ((MDCD)_obj).Orientation;
                    case MAD_DataType.Mimic:
                        return ((Mimic)_obj).Data.Orientation.Us;
                    case MAD_DataType.Decoration:
                        return ((Decoration)_obj).Orientation.Us;
                    default:
                        return default;
                }
            }
            set
            {
                switch (_dataType)
                {
                    case MAD_DataType.None:
                        ((MDCD)_obj).Orientation = value;
                        break;
                    case MAD_DataType.Mimic:
                        ((Mimic)_obj).Data.Orientation.Us = value;
                        break;
                    case MAD_DataType.Decoration:
                        ((Decoration)_obj).Orientation.Us = value;
                        break;
                }
            }
        }

        public int ColorIndex
        {
            get
            {
                switch (_dataType)
                {
                    case MAD_DataType.None:
                        return ((MDCD)_obj).ColorIndex;
                    case MAD_DataType.Mimic:
                        return ((Mimic)_obj).color;
                    case MAD_DataType.Decoration:
                        return ((Decoration)_obj).Color.Us;
                    default:
                        return default;
                }
            }
            set
            {
                switch (_dataType)
                {
                    case MAD_DataType.None:
                        ((MDCD)_obj).ColorIndex = value;
                        break;
                    case MAD_DataType.Mimic:
                        ((Mimic)_obj).SetColor(value);
                        break;
                    case MAD_DataType.Decoration:
                        ((Decoration)_obj).Color.Us = value;
                        break;
                }
            }
        }

        public Guid MaterialReplacement
        {
            get
            {
                switch (_dataType)
                {
                    case MAD_DataType.None:
                        return ((MDCD)_obj).MaterialReplacement;
                    case MAD_DataType.Mimic:
                        return ((Mimic)_obj).Data.MaterialReplacement.Us;
                    case MAD_DataType.Decoration:
                        return ((Decoration)_obj).MaterialReplacement.Us;
                    default:
                        return default;
                }
            }
            set
            {
                switch (_dataType)
                {
                    case MAD_DataType.None:
                        ((MDCD)_obj).MaterialReplacement = value;
                        break;
                    case MAD_DataType.Mimic:
                        ((Mimic)_obj).Data.MaterialReplacement.Us = value;
                        break;
                    case MAD_DataType.Decoration:
                        ((Decoration)_obj).MaterialReplacement.Us = value;
                        break;
                }
            }
        }



        public MimicAndDecorationCommonData()
        {
            _dataType = MAD_DataType.None;
            _obj = new MDCD();
        }

        public MimicAndDecorationCommonData(Mimic mimic)
        {
            _dataType = MAD_DataType.Mimic;
            _obj = mimic;
        }

        public MimicAndDecorationCommonData(Decoration decoration)
        {
            _dataType = MAD_DataType.Decoration;
            _obj = decoration;
        }
    }
}
