using System.Collections.Generic;
using System.Reflection;
using Funq;
using NUnit.Framework;
using ServiceStack.Data;
using ServiceStack.IO;
using ServiceStack.OrmLite;
using ServiceStack.Script;
using ServiceStack.Text;

namespace ServiceStack.WebHost.Endpoints.Tests.ScriptTests
{
    public class ScriptLispApiTests
    {
        private static ScriptContext CreateContext()
        {
            var context = new ScriptContext {
                ScriptLanguages = {ScriptLisp.Language},
                Args = {
                    ["nums3"] = new[] {0, 1, 2},
                    ["nums10"] = new[] {0, 1, 2, 3, 4, 5, 6, 7, 8, 9,},
                }
            };
            return context.Init();
        }

        [SetUp]
        public void Setup() => context = CreateContext();

        private ScriptContext context;

        string render(string lisp) => context.RenderLisp(lisp).NormalizeNewLines();
        void print(string lisp) => render(lisp).Print();
        object eval(string lisp) => context.EvaluateLisp($"(return (let () {lisp}))");

        [Test]
        public void LISP_even()
        {
            Assert.That(eval(@"(even? 2)"), Is.True);
            Assert.That(eval(@"(even? 1)"), Is.Null);
        }

        [Test]
        public void LISP_odd()
        {
            Assert.That(eval(@"(odd? 2)"), Is.Null);
            Assert.That(eval(@"(odd? 1)"), Is.True);
        }

        [Test]
        public void LISP_mapcan()
        {
            Assert.That(eval(@"(mapcan (lambda (x) (and (number? x) (list x))) '(a 1 b c 3 4 d 5))"),
                Is.EqualTo(new[] {1, 3, 4, 5}));
        }

        [Test]
        public void LISP_filter()
        {
            Assert.That(eval(@"(filter even? (range 10))"), Is.EqualTo(new[] {0, 2, 4, 6, 8}));
        }

        [Test]
        public void LISP_doseq()
        {
            Assert.That(render(@"(doseq (x nums3) (println x))"), Is.EqualTo("0\n1\n2"));
        }
 
        [Test]
        public void LISP_map_literals()
        {
            var expected = new Dictionary<string, object> {
                {"a", 1},
                {"b", 2},
                {"c", 3},
            };
            Assert.That(eval("(new-map '(a 1) '(b 2) '(c 3) )"), Is.EqualTo(expected));
            Assert.That(eval("{ :a 1 :b 2 :c 3 }"), Is.EqualTo(expected));
            Assert.That(eval("{ :a 1, :b 2, :c 3 }"), Is.EqualTo(expected));

            Assert.That(eval("{ :a 1  :b { :b1 10 :b2 20 }  :c 3 }"), Is.EqualTo(new Dictionary<string, object> {
                {"a", 1},
                {"b", new Dictionary<string, object> {
                    {"b1", 10},
                    {"b2", 20},
                }},
                {"c", 3},
            }));
        }

        [Test]
        public void LISP_can_clojure_fn_data_list_args()
        {
            Assert.That(render(@"(defn f [] 0)(f)"), Is.EqualTo("0"));
            Assert.That(render(@"(defn f [a] a)(f 1)"), Is.EqualTo("1"));
            Assert.That(render(@"(defn f [a b] (+ a b))(f 1 2)"), Is.EqualTo("3"));
            Assert.That(render(@"(defn f [a b c] (+ a b c))(f 1 2 3)"), Is.EqualTo("6"));
            Assert.That(render(@"((fn [a b c] (+ a b c)) 1 2 3)"), Is.EqualTo("6"));
        }

        [Test]
        public void LISP_can_call_xml_ContextBlockFilter()
        {
            var obj = eval(@"(/xml { :a 1 } )");
            Assert.That(obj, Does.Contain("<Key>a</Key>"));
        }

        [Test]
        public void LISP_test_void_variable()
        {
            Assert.That(eval(@"(if (bound? id) 1 -1)"), Is.EqualTo(-1));
            Assert.That(eval(@"(setq id 2)(if (bound? id) 1 -1)"), Is.EqualTo(1));

            Assert.That(eval(@"(if (bound? id id2) 1 -1)"), Is.EqualTo(-1));
            Assert.That(eval(@"(setq id 2)(if (bound? id id2) 1 -1)"), Is.EqualTo(-1));
            Assert.That(eval(@"(setq id 2)(setq id2 3)(if (bound? id id2) 1 -1)"), Is.EqualTo(1));
        }

        [Test]
        public void Can_access_page_vars()
        {
            Assert.That(context.EvaluateLisp(@"<!--
id 1
-->

(return (let () 
    (if (bound? id) 1 -1)

))"), Is.EqualTo(1));
            
        }

        [Test]
        public void Can_access_page_vars_with_line_comment_prefix()
        {
            Assert.That(context.EvaluateLisp(@";<!--
; id 1
;-->

(return (let () 
    (if (bound? id) 1 -1)

))"), Is.EqualTo(1));
            
        }

        [Test]
        public void LISP_string_format()
        {
            Assert.That(render(@"(/fmt ""{0} + {1} = {2}"" 1 2 (+ 1 2))"), 
                Is.EqualTo("1 + 2 = 3"));
        }

        [Test]
        public void Can_access_db()
        {
            var context = new ScriptContext {
                ScriptLanguages = { ScriptLisp.Language },
                ScriptMethods = {
                    new DbScriptsAsync()
                }
            };
            context.Container.AddSingleton<IDbConnectionFactory>(() => 
                new OrmLiteConnectionFactory(":memory:", SqliteDialect.Provider));
            context.Init();

            using (var db = context.Container.Resolve<IDbConnectionFactory>().Open())
            {
                db.CreateTable<Person>();
                
                db.InsertAll(new [] {
                    new Person("A", 1), 
                    new Person("B", 2), 
                });
            }

            var result = context.EvaluateLisp(
                @"(return (map (fn [p] (:Name p)) (/dbSelect ""select Name, Age from Person"")))");
            Assert.That(result, Is.EqualTo(new[] { "A", "B" }));
        }

        [Test]
        public void Can_load_scripts()
        {
            var context = new ScriptContext {
                ScriptLanguages = { ScriptLisp.Language },
                ScriptMethods = { new ProtectedScripts() },
            }.Init();
            
            context.VirtualFiles.WriteFile("lib1.l", "(defn lib-calc [a b] (+ a b))");
            context.VirtualFiles.WriteFile("/dir/lib2.l", "(defn lib-calc [a b] (* a b))");

            object result;
            
            result = context.EvaluateLisp(@"(load 'lib1)(return (lib-calc 4 5))");
            Assert.That(result, Is.EqualTo(9));
            
            result = context.EvaluateLisp(@"(load ""lib1.l"")(return (lib-calc 4 5))");
            Assert.That(result, Is.EqualTo(9));
            
            result = context.EvaluateLisp(@"(load ""/dir/lib2.l"")(return (lib-calc 4 5))");
            Assert.That(result, Is.EqualTo(20));
            result = context.EvaluateLisp(@"(load 'lib1)(load ""/dir/lib2.l"")(return (lib-calc 4 5))");
            Assert.That(result, Is.EqualTo(20));
            
            // https://gist.github.com/gistlyn/2f14d629ba1852ee55865607f1fa2c3e
        }

//        [Test] // skip integration test
        public void Can_load_scripts_from_gist_and_url()
        {
//            Lisp.AllowLoadingRemoteScripts = false; // uncomment to prevent loading remote scripts

            var context = new ScriptContext {
                ScriptLanguages = { ScriptLisp.Language },
                ScriptMethods = { new ProtectedScripts() },
            }.Init();
            

            LoadLispTests(context);
            LoadLispTests(context); // load twice to check it's using cached downloaded assets
        }

        private static void LoadLispTests(ScriptContext context)
        {
            object result;

            result = context.EvaluateLisp(@"(load ""gist:2f14d629ba1852ee55865607f1fa2c3e/lib1.l"")(return (lib-calc 4 5))");
            Assert.That(result, Is.EqualTo(9));

            // imports all gist files and overwrites symbols where last symbol wins
            result = context.EvaluateLisp(@"(load ""gist:2f14d629ba1852ee55865607f1fa2c3e"")(return (lib-calc 4 5))");
            Assert.That(result, Is.EqualTo(20));

            result = context.EvaluateLisp(
                @"(load ""https://gist.githubusercontent.com/gistlyn/2f14d629ba1852ee55865607f1fa2c3e/raw/95cbc5d071d9db3a96866c1a583056dd87ab5f69/lib1.l"")(return (lib-calc 4 5))");
            Assert.That(result, Is.EqualTo(9));

            // import single file from index.md
            result = context.EvaluateLisp(@"(load ""index:lib-calc/lib1.l"")(return (lib-calc 4 5))");
            Assert.That(result, Is.EqualTo(9));

            // imports all gist files and overwrites symbols where last symbol wins
            result = context.EvaluateLisp(@"(load ""index:lib-calc"")(return (lib-calc 4 5))");
            Assert.That(result, Is.EqualTo(20));
        }
    }
    
/* If LISP integration tests are needed in future
    public class ScriptListAppHostTests
    {
        private ServiceStackHost appHost;

        class AppHost : AppSelfHostBase
        {
            public AppHost() : base(nameof(ScriptListAppHostTests), typeof(ScriptListAppHostTests).Assembly) { }
            public override void Configure(Container container)
            {
                Plugins.Add(new SharpPagesFeature {
                    ScriptLanguages = { ScriptLisp.Language },
                });
            }
        }

        public ScriptListAppHostTests()
        {
            appHost = new AppHost().Init(); 
        }

        [OneTimeTearDown]
        public void OneTimeTearDown() => appHost.Dispose();
 
        string render(string lisp) => appHost.GetPlugin<SharpPagesFeature>().RenderLisp(lisp).NormalizeNewLines();
        object eval(string lisp) => appHost.GetPlugin<SharpPagesFeature>().EvaluateLisp($"(return {lisp})");

//        [Test]
        public void Can_call_urlContents()
        {
            var output = render(@"(/urlContents ""https://api.github.com/repos/ServiceStack/ServiceStack"" { :userAgent ""#Script"" } )");
            output.Print();
        }
    }
*/
}