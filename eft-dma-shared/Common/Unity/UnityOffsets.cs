namespace eft_dma_shared.Common.Unity
{
    public readonly struct UnityOffsets
    {
        public readonly struct Component
        {
            public const uint Size = 0x38; // Equal to sizeof(Unity::Component) __This is not a struct field__
            public static readonly uint[] To_NativeClassName = new uint[] { 0x0, 0x0, 0x48 }; // String
            public const uint GameObject = 0x30; // To GameObject
        }        
        public readonly struct ModuleBase
        {
            public const uint GameObjectManager = 0x1CF93E0; // to eft_dma_radar.GameObjectManager
            public const uint AllCameras = 0x1BF8BC0; // Lookup in IDA 's_AllCamera'
            public const uint InputManager = 0x1C91748;
            public const uint GfxDevice = 0x1CF9F48; // g_MainGfxDevice , Type GfxDeviceClient
        }
        public readonly struct UnityInputManager
        {
            public const uint CurrentKeyState = 0x60; // 0x50 + 0x8
        }
        public readonly struct TransformInternal
        {
            public const uint TransformAccess = 0x38; // to TransformHierarchy
        }
        public readonly struct TransformAccess
        {
            public const uint Vertices = 0x18; // MemList<TrsX>
            public const uint Indices = 0x20; // MemList<int>
        }
        public readonly struct SkinnedMeshRenderer // SkinnedMeshRenderer : Renderer
        {
            public const uint Renderer = 0x10; // Renderer : Unity::Component
        }
        public readonly struct Renderer // Renderer : Unity::Component
        {
            public const uint Materials = 0x148; // m_Materials : dynamic_array<PPtr<Material>,0>
            public const uint Count = 0x158; // Extends from m_Materials type (0x20 length?)
        }
        public readonly struct Behaviour
        {
            public const uint IsEnabled = 0x38; // bool, Behaviour : m_Enabled
            public const uint IsAdded = 0x39; // bool, Behaviour : m_IsAdded
        }
        public readonly struct Camera
        {
            // CopiableState struct begins at 0x40
            public const uint ViewMatrix = 0x100;
            public const uint FOV = 0x180;
            public const uint LastPosition = 0x454;
            public const uint AspectRatio = 0x4F0;
            public const uint OcclusionCulling = 0x524; // bool, Camera::CopiableState -> m_OcclusionCulling
            public const uint NearClip = 0x464; // float (m_NearClip)
        }

        public readonly struct GfxDeviceClient
        {
            public const uint Viewport = 0x25A0; // m_Viewport      RectT<int> ?
        }

        public readonly struct UnityAnimator // Animator        struc ; (sizeof=0x6A0, align=0x8, copyof_18870)
        {
            public const uint Speed = 0x488; // 0000047C m_Speed
        }

        public readonly struct SSAA // Unity.Postprocessing.Runtime Assembly in UNISPECT
        {
            public const uint OpticMaskMaterial = 0x60; // -.SSAA->_opticMaskMaterial // Offset: 0x0060 (Type: UnityEngine.Material)
        }
        public readonly struct UnityString
        {
            public const uint Length = 0x10; // int32
            public const uint Value = 0x14; // string,unicode
        }
        public readonly struct UnityList
        {
            public const uint Base = 0x10; // to UnityListBase
            public const uint Count = 0x18; // int32
        }

        public readonly struct UnityListBase
        {
            public const uint Start = 0x20; // start of list +(i * 0x8)
        }
        public readonly struct BaseRenderer
        {
            public const uint RendererData = 0x8; // SharedRendererData (m_RendererData)
        }

        public readonly struct SharedRendererData
        {
            public const uint TransformInfo = 0x0; // TransformInfo (m_TransformInfo)
        }

        public readonly struct TransformInfo
        {
            public const uint LocalAABB = 0x98; // AABB (localAABB)
        }             
    }
}
