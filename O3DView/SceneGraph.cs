namespace O3DView
{
    public class SceneGraph
    {
        public Camera camera = new();
        // TODO: Make this a graph, not just a list... (makes computing transforms more complicated)
        public List<Light> lights = [];
        public List<Mesh> meshes = [];
        public readonly Dictionary<int, Material> materialCache = [];
    }

    public class SceneObject
    {
        public Transform transform = new();
        public string name = "SceneObj";
    }
}
