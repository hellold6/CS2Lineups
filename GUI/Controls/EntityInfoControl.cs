using System.Windows.Forms;
using GUI.Utils;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace GUI.Forms
{
    partial class EntityInfoControl : UserControl
    {
        public DataGridView OutputsGrid => dataGridOutputs;

        public EntityInfoControl()
        {
            InitializeComponent();

            components ??= new System.ComponentModel.Container();
            components.Add(tabPageOutputs);
        }

        public void ShowPropertiesTab()
        {
            tabControl.SelectedIndex = 0;
        }

        public void ShowOutputsTabIfAnyData()
        {
            if (dataGridOutputs.RowCount > 0)
            {
                if (tabPageOutputs.Parent == null)
                {
                    tabControl.TabPages.Add(tabPageOutputs);
                }
            }
            else
            {
                if (tabPageOutputs.Parent != null)
                {
                    tabControl.TabPages.Remove(tabPageOutputs);
                }
            }
        }

        public void Clear()
        {
            dataGridProperties.Rows.Clear();
            dataGridOutputs.Rows.Clear();
        }

        public void PopulateFromEntity(Entity entity)
        {
            foreach (var child in entity.Children)
            {
                AddProperty(child.Key, StringifyValue(child.Value));
            }

            if (entity.Connections != null)
            {
                foreach (var connection in entity.Connections)
                {
                    AddConnection(connection);
                }
            }
        }

        public void AddProperty(string name, string value)
        {
            dataGridProperties.Rows.Add([name, value]);
        }

        public void AddConnection(KVObject connectionData)
        {
            var outputName = connectionData.GetStringProperty("m_outputName");
            var targetName = connectionData.GetStringProperty("m_targetName");
            var inputName = connectionData.GetStringProperty("m_inputName");
            var parameter = connectionData.GetStringProperty("m_overrideParam");
            var delay = connectionData.GetFloatProperty("m_flDelay");
            var timesToFire = connectionData.GetInt32Property("m_nTimesToFire");

            var stimesToFire = timesToFire switch
            {
                1 => "Only Once",
                >= 2 => $"Only {timesToFire} Times",
                _ => "Infinite",
            };

            dataGridOutputs.Rows.Add([
                outputName,
                targetName,
                inputName,
                parameter,
                delay,
                stimesToFire
            ]);
        }
    }
}
