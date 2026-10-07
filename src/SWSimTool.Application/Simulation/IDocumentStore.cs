namespace SWSimTool.Simulation {
    // Configuration transport is opaque to the domain and workflow layers.
    public interface IDocumentStore {
        string ReadConfiguration();
        void WriteConfiguration(string payload);
    }
}
