#pragma warning disable CS0649

using System.Collections.Generic;

namespace Ghbvft6.Calq.ServerTest {
    public class TestService {
        public class Nested {
            public int a = 1;
            public int b;
        }

        public TestService() {
            list.Add(1);
            list.Add(2);
            dictionary.Add(0, 1);
            dictionary.Add(1, 2);
            listOfObjects.Add(new Nested());
            listOfObjects.Add(new Nested());
        }

        public int integer;
        public bool boolean;
        public Nested nested = new();
        public Nested nullNested;
        public string text = "text";
        public string nullText;
        public int[] array = new[] { 1, 2 };
        public List<int> list = new();
        public List<Nested> listOfObjects = new();
        public Dictionary<int, int> dictionary = new();
    }
}
