# **Архитектурный план и детальное руководство по разработке пользовательской альтернативы библиотеки AutoMapper с поддержкой EF Core Projectables**

## **Введение и концептуальный обзор предметной области**

Разработка собственной альтернативы библиотеке AutoMapper представляет собой комплексную архитектурную задачу, требующую глубокого понимания механизмов рефлексии (Reflection), генерации деревьев выражений (Expression Trees) в платформе.NET, а также внутреннего устройства конвейера трансляции запросов Entity Framework Core (EF Core). Инструменты объектно-объектного маппинга исторически предназначены для трансформации входных объектов одного типа в выходные объекты другого типа, чаще всего в сценариях проецирования сложных доменных моделей базы данных в легковесные объекты передачи данных (DTO).1 Использование подобных библиотек позволяет избежать написания рутинного кода ручного маппинга, который затрудняет поддержку приложения и нарушает принцип единственной ответственности.2

Пользовательский запрос устанавливает жесткие и специфические критерии для создаваемой библиотеки, которые выходят за рамки простого копирования свойств в памяти. Во-первых, необходимо обеспечить поддержку строго определенного синтаксиса конфигурации на основе базового класса Profile с методами CreateMap и ForMember, полностью повторяющего эргономику AutoMapper.4 Во-вторых, архитектура должна гарантировать, что экземпляры профилей регистрируются в контейнере внедрения зависимостей (Dependency Injection) исключительно как Singleton-сервисы, что критично для производительности и оптимизации использования памяти.6 В-третьих, требуется внедрить механизм передачи внешних параметров на этапе выполнения (runtime) — таких как язык локализации или динамические названия справочных таблиц — в конфигурацию маппинга, не нарушая при этом жизненный цикл Singleton и правила трансляции EF Core.8 Наконец, создаваемая библиотека должна обладать нативной и бесшовной совместимостью с расширением EntityFrameworkCore.Projectables, обеспечивая корректную трансляцию инкапсулированных вычисляемых свойств и методов непосредственно в оптимизированные SQL-запросы.10

Данный отчет представляет собой исчерпывающий технический проект реализации, охватывающий абстрактное проектирование API, управление жизненным циклом компонентов, низкоуровневые механизмы генерации абстрактных синтаксических деревьев и нюансы интеграции с конвейером компиляции запросов EF Core.

## **Теоретические основы проецирования и трансформации выражений**

Прежде чем переходить к проектированию интерфейсов, необходимо четко разграничить два фундаментально разных подхода к маппингу данных в.NET: маппинг объектов в памяти (In-memory mapping) и проецирование запросов к базе данных (Queryable projection). Эти два механизма требуют совершенно разной архитектурной реализации, несмотря на то, что для конечного пользователя они могут выглядеть идентично.

Маппинг в памяти работает с объектами, которые уже загружены в оперативную память (например, представлены в виде IEnumerable\<T\>). В этом сценарии маппер использует скомпилированные делегаты (Func\<TSource, TDest\>), которые извлекают значения из исходного объекта и присваивают их целевому. Это быстрый процесс, но если он применяется к сущностям EF Core, он вызывает проблему избыточной выборки данных (over-fetching). База данных возвращает все столбцы таблицы, фреймворк материализует полные графы объектов, и только потом маппер отбрасывает ненужные свойства, формируя DTO.3

Проецирование запросов, с другой стороны, работает на уровне интерфейса IQueryable\<T\> и деревьев выражений Expression\<Func\<TSource, TDest\>\>. Деревья выражений — это неизменяемые структуры данных, которые представляют программный код в виде древовидной структуры узлов, где каждый узел описывает определенную операцию (например, доступ к свойству, вызов метода или бинарную операцию).12 В отличие от делегатов, выражения не содержат скомпилированного IL-кода; они существуют для того, чтобы сторонние провайдеры (такие как EF Core) могли проанализировать их логику во время выполнения.14

Когда пользователь вызывает метод проецирования (эквивалент ProjectTo в AutoMapper), библиотека маппинга должна динамически сгенерировать дерево выражений, описывающее трансформацию, и внедрить его в вызов метода Queryable.Select.9 Провайдер EF Core получает это дерево выражений, обходит его с помощью собственных реализаций ExpressionVisitor и транслирует в SQL-запрос. В результате база данных возвращает только те столбцы, которые действительно необходимы для DTO, что радикально повышает производительность и снижает нагрузку на сеть.16 Начиная с версии EF Core 3.0, фреймворк строго запрещает клиентскую оценку (client-side evaluation) в любой части запроса, кроме конечной проекции на верхнем уровне, что означает: если маппер сгенерирует выражение, которое EF Core не сможет перевести в SQL, во время выполнения будет выброшено исключение InvalidOperationException.18

Таблица 1\. Сравнение парадигм маппинга данных в экосистеме.NET.

| Характеристика | Маппинг в памяти (IEnumerable) | Проецирование запросов (IQueryable) |
| :---- | :---- | :---- |
| **Базовый тип трансформации** | Func\<TSource, TDest\> | Expression\<Func\<TSource, TDest\>\> |
| **Время выполнения логики** | На стороне клиента (память сервера приложений) | На стороне базы данных (в виде SQL-кода) |
| **Объем передаваемых данных** | Загрузка всей сущности из БД (SELECT \*) | Загрузка только нужных столбцов (SELECT A, B) |
| **Совместимость с EF Core** | Требует предварительного вызова .ToList() | Нативно интегрируется в конвейер трансляции |
| **Поддержка C\# методов** | Полная (любой C\# код) | Ограниченная (только транслируемые операции) |

Понимание этого фундаментального различия определяет всю дальнейшую архитектуру. Новая библиотека должна фокусироваться исключительно на генерации валидных, транслируемых деревьев выражений, минимизируя использование непереводимых пользовательских конструкций.

## **Проектирование архитектуры конфигурации и Fluent API**

Основой пользовательского опыта при работе с маппером является декларативное описание правил преобразования. Пользовательский запрос содержит четкий пример того, как должен выглядеть программный интерфейс. Требуется реализовать базовый класс Profile, внутри конструктора которого вызываются типизированные методы конфигурации.

### **Структура класса Profile и накопление метаданных**

Класс Profile выступает в роли строителя (Builder) для метаданных маппинга. Важно понимать, что внутри конструктора Profile преобразование данных не происходит. Единственная цель этого класса — создать и сохранить абстрактное описание того, как свойства источника соотносятся со свойствами назначения.1

Для реализации заявленного синтаксиса CreateMap\<spCategory, viCatigory\>() необходимо разработать внутреннюю инфраструктуру хранения выражений. Базовый класс Profile должен содержать коллекцию абстрактных конфигураций типов:

C\#

public abstract class Profile  
{  
    // Внутреннее хранилище конфигураций маппинга для данного профиля  
    internal readonly List\<ITypeMapConfiguration\> TypeMaps \= new List\<ITypeMapConfiguration\>();

    protected IMappingExpression\<TSource, TDestination\> CreateMap\<TSource, TDestination\>()  
    {  
        var mappingExpression \= new MappingExpression\<TSource, TDestination\>();  
        TypeMaps.Add(mappingExpression);  
        return mappingExpression;  
    }  
}

Этот подход строго изолирует этап сбора конфигурации (Design-time) от этапа выполнения (Run-time). Метод CreateMap возвращает интерфейс IMappingExpression\<TSource, TDestination\>, который в свою очередь реализует паттерн Fluent Builder для цепочки вызовов ForMember.

### **Механика метода ForMember**

Метод ForMember является центральным элементом Fluent API. Согласно исходным требованиям, его сигнатура должна позволять указывать свойство назначения с помощью лямбда-выражения, а конфигурацию источника — с помощью делегата настройки.20

Определение интерфейсов, обеспечивающих такую эргономику, требует использования параметризованных делегатов действий (Action):

C\#

public interface IMappingExpression\<TSource, TDestination\> : ITypeMapConfiguration  
{  
    IMappingExpression\<TSource, TDestination\> ForMember\<TMember\>(  
        Expression\<Func\<TDestination, TMember\>\> destinationMember,  
        Action\<IMemberConfigurationExpression\<TSource, TDestination, TMember\>\> memberOptions);  
}

public interface IMemberConfigurationExpression\<TSource, TDestination, TMember\>  
{  
    void MapFrom\<TSourceMember\>(Expression\<Func\<TSource, TSourceMember\>\> mapExpression);  
}

Когда пользователь пишет .ForMember(des \=\> des.NameUz, o \=\> o.MapFrom(sou \=\> sou.NameUz)), библиотека выполняет следующие действия:

1. Анализирует выражение des \=\> des.NameUz (которое является типом MemberExpression) для извлечения метаданных о свойстве NameUz целевого класса.14  
2. Инициализирует внутренний класс конфигурации члена, реализующий IMemberConfigurationExpression.  
3. Передает этот класс в пользовательский делегат memberOptions.  
4. Внутри делегата пользователь вызывает MapFrom, передавая выражение источника sou \=\> sou.NameUz.  
5. Библиотека сохраняет полученное выражение в словарь привязок (Property Bindings) внутри TypeMap.

Эта архитектура позволяет накапливать разрозненные пользовательские лямбда-выражения для каждого свойства. На более поздних этапах эти выражения будут извлечены, их параметры будут унифицированы, и они будут объединены в единое дерево инициализации объекта, пригодное для передачи в Entity Framework Core.23

## **Управление жизненным циклом и Dependency Injection (Singleton)**

Современные приложения.NET полагаются на контейнер внедрения зависимостей (Dependency Injection) для управления жизненным циклом компонентов. Пользовательский запрос устанавливает строгое требование: профиль конфигурации должен быть зарегистрирован как Singleton.

### **Регистрация конфигурации в DI-контейнере**

Регистрация профилей в качестве Singleton-сервисов является фундаментальной архитектурной практикой, обеспечивающей оптимальное использование ресурсов.6 Компиляция конфигурации маппинга и генерация деревьев выражений — это ресурсоемкие операции, связанные с рефлексией. Если выполнять их при каждом HTTP-запросе (при Transient или Scoped жизненном цикле), приложение столкнется с катастрофическим падением производительности.

Для обеспечения правильной инициализации необходимо разработать метод расширения для IServiceCollection, который будет сканировать сборки приложения на наличие классов, унаследованных от Profile, инстанцировать их и компилировать единую конфигурацию.

C\#

public static class MapperServiceCollectionExtensions  
{  
    public static IServiceCollection AddCustomMapper(this IServiceCollection services, params Assembly assemblies)  
    {  
        // Использование рефлексии для поиска всех реализаций Profile  
        var profileTypes \= assemblies.SelectMany(a \=\> a.GetTypes())  
           .Where(t \=\> typeof(Profile).IsAssignableFrom(t) &&\!t.IsAbstract);

        var engineConfiguration \= new MapperEngineConfiguration();

        foreach (var type in profileTypes)  
        {  
            // Создание экземпляра профиля (вызов конструктора)  
            var profileInstance \= (Profile)Activator.CreateInstance(type);  
            engineConfiguration.AddProfile(profileInstance);  
        }

        // Предварительная компиляция и проверка выражений  
        engineConfiguration.CompileMappings();

        // Регистрация скомпилированной конфигурации как строгого Singleton  
        services.AddSingleton\<IMapperConfiguration\>(engineConfiguration);  
          
        // Регистрация исполнителя маппинга  
        services.AddScoped\<IMapper, MapperEngine\>();

        return services;  
    }  
}

В данной модели конфигурация (IMapperConfiguration) живет в течение всего времени работы приложения. При этом сам исполнитель (IMapper) регистрируется как Scoped. Это разделение жизненных циклов решает критически важную проблему инъекции параметров.

### **Проблема "Captive Dependency" и пути ее решения**

Требование поддержки внешних параметров, таких как язык локализации для конкретного HTTP-запроса, вступает в прямое противоречие с концепцией Singleton. Если попытаться внедрить Scoped-зависимость (например, сервис получения текущего пользователя или языка) напрямую в конструктор Singleton-профиля, DI-контейнер ASP.NET Core выбросит исключение System.InvalidOperationException: Cannot consume scoped service from singleton.25 Даже если бы контейнер позволил это сделать, значение языка было бы зафиксировано в момент запуска приложения для самого первого запроса и оставалось бы неизменным для всех последующих пользователей (проблема Captive Dependency).27

Следовательно, архитектура профиля не должна содержать никакого состояния, зависящего от контекста запроса. Профили не должны инжектировать в себя внешние сервисы.6 Вместо этого профиль должен описывать конфигурацию декларативно, с использованием отложенных маркеров (placeholders) параметров, которые будут разрешаться на этапе выполнения запроса объектом IMapper.

Таблица 2\. Разделение ответственности и жизненных циклов в архитектуре маппера.

| Компонент | Жизненный цикл | Ответственность | Состояние (State) |
| :---- | :---- | :---- | :---- |
| Profile | Singleton (через фабрику) | Определение правил маппинга (DSL) | Неизменяемое (Immutable) после инициализации |
| IMapperConfiguration | Singleton | Хранение кэшированных деревьев выражений | Потокобезопасный кэш словарей |
| IMapper | Scoped / Transient | Выполнение проекции, передача контекста | Содержит словарь параметров текущего запроса |

## **Динамические параметры времени выполнения в проецировании**

Реализация требования передачи внешних параметров в профиль — язык локализации или названия справочных таблиц — является наиболее сложной задачей в контексте интеграции с EF Core. В традиционном маппинге в памяти AutoMapper предоставляет механизм opt.Items или ResolutionContext для передачи произвольных словарей данных.8 Однако, если попытаться использовать подобные C\#-контексты внутри LINQ-запроса EF Core, провайдер базы данных не сможет перевести вызов context.Items\["Language"\] в SQL-код и прервет выполнение.18

Для решения этой проблемы требуется реализовать паттерн "Параметризованная проекция с поздним связыванием" через деревья выражений.

### **Декларация параметров в профиле**

В конструкторе CatigoryProfile пользователь должен иметь возможность указать, что определенное свойство вычисляется на основе внешнего параметра. Для этого API расширяется дополнительным классом контекста, который выступает в роли синтаксического маркера:

C\#

public class CatigoryProfile : Profile  
{  
    public CatigoryProfile()  
    {  
        CreateMap\<spCategory, viCatigory\>()  
           .ForMember(des \=\> des.Id, o \=\> o.MapFrom(sou \=\> sou.Id))  
            // Добавление перегрузки MapFrom, принимающей контекст с параметрами  
           .ForMember(des \=\> des.NameLocal, o \=\> o.MapFrom((sou, ctx) \=\>   
                ctx.GetValue\<string\>("Language") \== "uz"? sou.NameUz :   
                ctx.GetValue\<string\>("Language") \== "lt"? sou.NameLt :   
                sou.NameRu))  
            // Пример динамического выбора справочной таблицы  
           .ForMember(des \=\> des.RefData, o \=\> o.MapFrom((sou, ctx) \=\>   
                ctx.GetValue\<string\>("RefTable") \== "TableA"? sou.TableAReference : sou.TableBReference));  
    }  
}

Выражение (sou, ctx) \=\> ctx.GetValue\<string\>("Language") \== "uz"?... сохраняется в метаданных профиля. Но оно не может быть отправлено в EF Core в таком виде.

### **Подстановка параметров через ExpressionVisitor**

Механизм решения заключается в модификации дерева выражений непосредственно перед его передачей в провайдер IQueryable. Когда вызывается метод выполнения проекции, пользователь передает анонимный объект или словарь с реальными значениями параметров для текущего запроса.9

C\#

// Использование в контроллере или сервисе  
var language \= \_currentUserService.Language; // "uz"  
var query \= dbContext.Categories  
   .ProjectTo\<viCatigory\>(\_mapper, new { Language \= language, RefTable \= "TableA" });

Внутри метода ProjectTo создаваемая библиотека должна задействовать специальный класс-наследник ExpressionVisitor для переписывания дерева выражений. Шаблон Visitor (Посетитель) позволяет рекурсивно обойти все узлы дерева выражений и заменить определенные узлы на новые.30

Алгоритм работы ParameterInjectionVisitor:

1. Визитор принимает словарь реальных параметров (например, {"Language": "uz"}).  
2. Он обходит базовое закэшированное дерево маппинга.  
3. При обнаружении узла типа MethodCallExpression, который представляет вызов ctx.GetValue\<T\>("Key"), визитор извлекает строковый ключ ("Language").  
4. Визитор ищет этот ключ в словаре реальных параметров.  
5. Если значение найдено, визитор удаляет весь вызов метода из дерева и заменяет его на ConstantExpression (константное выражение), содержащее реальное значение (например, "uz").33

Техническая реализация визитора подстановки:

C\#

public class ParameterInjectionVisitor : ExpressionVisitor  
{  
    private readonly IDictionary\<string, object\> \_runtimeParameters;

    public ParameterInjectionVisitor(IDictionary\<string, object\> parameters)  
    {  
        \_runtimeParameters \= parameters;  
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)  
    {  
        // Проверка, является ли вызываемый метод ctx.GetValue\<T\>(string key)  
        if (node.Method.Name \== "GetValue" && node.Object?.Type \== typeof(IMappingContext))  
        {  
            // Извлечение аргумента ключа (например, "Language")  
            if (node.Arguments is ConstantExpression keyExpression &&   
                keyExpression.Value is string key)  
            {  
                if (\_runtimeParameters.TryGetValue(key, out var value))  
                {  
                    // Трансформация вызова метода в строго типизированную константу  
                    return Expression.Constant(value, node.Method.ReturnType);  
                }  
            }  
        }  
        return base.VisitMethodCall(node);  
    }  
}

В результате работы этого визитора абстрактное выражение превращается в строго определенное (sou) \=\> "uz" \== "uz"? sou.NameUz :....

Для Entity Framework Core такое выражение является тривиальным. Оптимизатор запросов EF Core способен оценить константные выражения и выполнить частичную оценку дерева. Поскольку "uz" \== "uz" всегда истинно, EF Core даже не будет транслировать конструкцию CASE WHEN в SQL; он упростит выражение на лету и сгенерирует чистый SELECT \[NameUz\] FROM Categories, отбрасывая мертвый код.17 Это обеспечивает максимальную производительность SQL-запроса при сохранении полной динамичности конфигурации маппинга.

Для "справочных таблиц" логика аналогична. Если маппинг использует тернарный оператор для выбора навигационного свойства на основе динамического имени таблицы, визитор заменит ключ на константу, и EF Core сгенерирует LEFT JOIN только к той таблице, ветвь которой оказалась активной после упрощения дерева.

## **Глубокое погружение в генерацию деревьев выражений**

Центральным ядром архитектуры, скрытым от конечного пользователя, является компилятор деревьев выражений. Его задача — взять разрозненные лямбда-выражения из ForMember и собрать из них единое MemberInitExpression, представляющее блок кода new viCatigory() {... }.23

### **Объединение параметров (Parameter Unification)**

Главная сложность генерации единого дерева заключается в согласовании областей видимости переменных (параметров лямбда-выражений). Каждый пользовательский вызов MapFrom(sou \=\> sou.Id) и MapFrom(sou \=\> sou.ImgUrl) создает собственную независимую лямбду со своим уникальным экземпляром ParameterExpression (представляющим переменную sou).

Невозможно просто взять тела этих лямбд и поместить их в одно общее выражение инициализации, так как они ссылаются на разные объекты параметров, даже если у них одинаковое имя и тип. EF Core выбросит ошибку компиляции дерева, указав, что параметр не определен в текущей области видимости.31

Следовательно, генератор должен создать один "глобальный" ParameterExpression для исходного типа (spCategory), а затем использовать специальный ExpressionVisitor для замены всех локальных параметров в каждом пользовательском выражении на этот глобальный параметр.38

C\#

public class ParameterReplaceVisitor : ExpressionVisitor  
{  
    private readonly ParameterExpression \_oldParameter;  
    private readonly ParameterExpression \_newParameter;

    public ParameterReplaceVisitor(ParameterExpression oldParameter, ParameterExpression newParameter)  
    {  
        \_oldParameter \= oldParameter;  
        \_newParameter \= newParameter;  
    }

    protected override Expression VisitParameter(ParameterExpression node)  
    {  
        // Замена уникального параметра лямбды на глобальный параметр выражения  
        return node \== \_oldParameter? \_newParameter : base.VisitParameter(node);  
    }  
}

### **Формирование MemberInitExpression**

После того как все выражения приведены к единому знаменателю, библиотека формирует узел создания объекта и инициализации его свойств.

C\#

public LambdaExpression BuildProjectionExpression(TypeMap typeMap)  
{  
    // 1\. Создание глобального параметра для сущности базы данных  
    var sourceParameter \= Expression.Parameter(typeMap.SourceType, "src");  
    var bindings \= new List\<MemberBinding\>();

    // 2\. Итерация по всем настроенным свойствам  
    foreach (var propertyMap in typeMap.PropertyMaps)  
    {  
        var destinationProperty \= typeMap.DestinationType.GetProperty(propertyMap.DestinationName);  
        var customExpression \= propertyMap.MapFromExpression;

        Expression mappedBody;  
        if (customExpression\!= null)  
        {  
            // Подмена параметров в пользовательском выражении  
            var visitor \= new ParameterReplaceVisitor(customExpression.Parameters, sourceParameter);  
            mappedBody \= visitor.Visit(customExpression.Body);  
        }  
        else  
        {  
            // Convention-based mapping: прямое копирование свойства по совпадению имен  
            var sourceProperty \= typeMap.SourceType.GetProperty(propertyMap.DestinationName);  
            if (sourceProperty\!= null)  
            {  
                mappedBody \= Expression.Property(sourceParameter, sourceProperty);  
            }  
            else continue; // Игнорировать, если свойство не найдено  
        }

        // 3\. Создание привязки свойства (Property Assignment)  
        var binding \= Expression.Bind(destinationProperty, mappedBody);  
        bindings.Add(binding);  
    }

    // 4\. Компиляция абстрактного кода: new DestinationType() { Prop \=... }  
    var newExpression \= Expression.New(typeMap.DestinationType);  
    var memberInit \= Expression.MemberInit(newExpression, bindings);

    // 5\. Обертывание в финальную лямбду  
    return Expression.Lambda(memberInit, sourceParameter);  
}

Этот фрагмент кода генерирует идеальное абстрактное синтаксическое дерево. Для коллекций дочерних объектов (когда свойство DTO представляет собой List\<ChildDto\>) алгоритм рекурсивно вызывает себя для построения дочернего MemberInitExpression, а затем оборачивает его в вызов метода Queryable.Select или Enumerable.Select с помощью Expression.Call.15 Это позволяет EF Core корректно генерировать сложные подзапросы или использовать механизм OUTER APPLY для загрузки иерархических данных из реляционной базы.

## **Нативная интеграция с EntityFrameworkCore.Projectables**

Одно из ключевых требований задачи — обеспечение поддержки библиотеки EntityFrameworkCore.Projectables. Чтобы понять, как новая архитектура маппера будет взаимодействовать с этим расширением, необходимо детально проанализировать принцип работы самого Projectables.

### **Механизм работы EF Core Projectables**

Библиотека EntityFrameworkCore.Projectables предназначена для решения классической проблемы EF Core: невозможности трансляции локально вычисляемых свойств или C\#-методов сущности в SQL.10 Обычно EF Core требует, чтобы вся логика находилась непосредственно внутри лямбда-выражения LINQ-запроса.

Projectables решает эту проблему элегантным архитектурным приемом, состоящим из двух независимых фаз:

1. **Source Generation (Генерация исходного кода на этапе компиляции):** Библиотека использует механизмы Roslyn Source Generators для поиска всех свойств и методов, помеченных атрибутом \[Projectable\]. Для каждого найденного члена автоматически генерируется теневой код (companion expression) — статическое свойство, содержащее эквивалентное Expression Tree.10 Например, если метод содержит логику return FirstName \+ " " \+ LastName;, генератор создает выражение x \=\> x.FirstName \+ " " \+ x.LastName.  
2. **Query Interception (Перехват запросов на этапе выполнения):** Когда пользователь настраивает dbContextOptions.UseProjectables(), библиотека внедряет собственный компонент IQueryTranslationPreprocessor в пайплайн трансляции запросов EF Core.41 Когда EF Core начинает транслировать запрос в SQL, этот препроцессор анализирует дерево выражений. Если он находит вызов метода или доступ к свойству, помеченному \[Projectable\], он извлекает сгенерированное на этапе компиляции дерево выражений и подменяет им непереводимый вызов прямо внутри абстрактного дерева запроса *до* того, как EF Core выбросит ошибку.10

Библиотека поддерживает два режима совместимости: "Full compatibility" (запросы разворачиваются до их передачи в ядро EF Core, что дает максимальную совместимость, но небольшие накладные расходы) и "Limited compatibility" (разворачивание происходит после принятия запроса EF Core, обеспечивая кэширование скомпилированного SQL и значительный прирост производительности).41

### **Бесшовная совместимость маппера и Projectables**

Уникальное архитектурное преимущество выбранного нами подхода (компиляция маппинга в чистые MemberInitExpression и внедрение их через Select) заключается в том, что наша библиотека **абсолютно и прозрачно совместима** с EFCore.Projectables. Для обеспечения этой совместимости не потребуется писать ни строчки специального интеграционного кода.

Это обусловлено порядком выполнения операций в конвейере.

Допустим, в исходной модели spCategory есть свойство, вычисляемое с помощью Projectables:

C\#

public partial class spCategory  
{  
    public string NameUz { get; set; }  
    public string ParentPrefix { get; set; }

    \[Projectable\]  
    public string GetFullCategoryName() \=\> ParentPrefix \+ " \- " \+ NameUz;  
}

В профиле конфигурации CatigoryProfile пользователь указывает маппинг:

C\#

.ForMember(des \=\> des.FullName, o \=\> o.MapFrom(sou \=\> sou.GetFullCategoryName()))

Когда наша библиотека генерирует Expression Tree, она без изменений копирует узел вызова метода sou.GetFullCategoryName() в итоговое выражение MemberInitExpression. Дерево выражений, сформированное нашим маппером, не пытается вычислить или перевести этот метод; оно просто хранит семантическую ссылку (узел MethodCallExpression) на этот метод.14

Затем наш метод расширения .ProjectTo\<viCatigory\>() внедряет это сгенерированное выражение в вызов Queryable.Select, передавая эстафету провайдеру EF Core.9

C\#

var query \= dbContext.Categories.ProjectTo\<viCatigory\>(\_mapper);  
// Внутреннее представление IQueryable на этом этапе:  
// dbContext.Categories.Select(src \=\> new viCatigory { FullName \= src.GetFullCategoryName() })

Поскольку провайдер EF Core уже сконфигурирован с .UseProjectables(), его внутренний препроцессор IQueryTranslationPreprocessor начинает обход полученного выражения.42 Он обнаруживает вызов GetFullCategoryName(), идентифицирует наличие атрибута \[Projectable\], обращается к сгенерированному двойнику из Source Generator и заменяет узел на src.ParentPrefix \+ " \- " \+ src.NameUz.

В результате, EF Core генерирует оптимизированный SQL-запрос (например, с использованием конкатенации \+ или CONCAT), объединяя мощность нашего маппера и магии препроцессора Projectables.10 Архитектура гарантирует, что любые методы с атрибутом \[Projectable\], включая расширения и методы с аргументами, будут корректно транслироваться в SQL.10

Важным ограничением для разработчиков новой библиотеки маппера является правило: ни в коем случае не пытаться принудительно компилировать пользовательские лямбда-выражения (Expression.Compile()) в процессе сборки дерева. Компиляция превратит абстрактный узел в черный ящик IL-инструкций, уничтожив метаданные, которые Projectables использует для идентификации методов. Все трансформации должны выполняться исключительно через ExpressionVisitor.

## **Оптимизация производительности и стратегии кэширования**

Поскольку библиотека маппера будет использоваться в самом горячем пути выполнения (critical path) — внутри запросов к базе данных, обслуживающих веб\-сервер, — архитектура должна обеспечивать максимальную производительность.

Сборка MemberInitExpression с использованием механизмов рефлексии (поиск свойств, создание узлов Expression) является чрезвычайно ресурсоемкой операцией. Выполнение этой процедуры при каждом HTTP-запросе приведет к существенным задержкам (десятков миллисекунд на запрос) и огромному давлению на сборщик мусора (Garbage Collector) из\-за выделения памяти под тысячи узлов абстрактного синтаксического дерева.13

### **Потокобезопасное кэширование деревьев**

Для решения этой проблемы необходимо реализовать строгий механизм кэширования в Singleton-классе MapperConfiguration. Дерево выражений для каждой уникальной пары "Источник-Назначение" должно строиться ровно один раз за время жизни приложения.

Идеальным инструментом для этого является ConcurrentDictionary\<TypePair, LambdaExpression\>.

Таблица 3\. Теоретическая оценка вычислительной сложности операций маппера.

| Этап конвейера | Структура данных | Вычислительная сложность | Частота выполнения |
| :---- | :---- | :---- | :---- |
| **Поиск Profile (Startup)** | Reflection API | ![][image1] (где ![][image2] \- число типов в сборке) | Однократно при запуске приложения |
| **Генерация базового дерева** | Expression API | ![][image3] (где ![][image4] \- число свойств объекта) | Однократно для каждой пары типов (лениво) |
| **Извлечение из кэша** | ConcurrentDictionary | ![][image5] | При каждом выполнении .ProjectTo() |
| **Подстановка параметров** | ExpressionVisitor | ![][image6] (где ![][image7] \- узлы с параметрами) | При каждом запросе (выполняется за доли миллисекунд) |

Когда пользователь вызывает dbContext.Categories.ProjectTo\<viCatigory\>(\_mapper, new { Language \= "uz" }), библиотека выполняет алгоритм за ![][image5]:

1. Обращается к кэшу и извлекает заранее построенное, сложное дерево MemberInitExpression.  
2. Передает это базовое дерево в ParameterInjectionVisitor. Визитор обходит дерево, ища только токены контекста, и создает *новую копию* дерева с подставленными константами. Операция клонирования дерева с подстановкой констант является очень быстрой, так как не требует рефлексии.  
3. Модифицированное дерево передается в EF Core.

Интеграция с EF Core добавляет еще один уровень кэширования. EF Core компилирует SQL-запросы на основе хеш-кода (Shape) переданного дерева выражений.42 Поскольку наш визитор подставляет константные узлы, форма дерева будет идентичной, независимо от того, передали ли мы Language \= "uz" или "ru". EF Core извлечет сгенерированный SQL из собственного Query Cache и просто выполнит параметризованный вызов sp\_executesql, обеспечивая производительность на уровне микросекунд и защиту от SQL-инъекций.

## **Технический регламент и пошаговый план разработки**

На основе глубокого архитектурного анализа предлагается детализированный пошаговый план создания библиотеки, готовый для передачи в отдел разработки.

### **Этап 1: Инфраструктура конфигурации и API**

На первом этапе создаются интерфейсы, определяющие границы библиотеки. Необходима реализация абстрактного класса Profile, в котором будет определен метод CreateMap\<TSource, TDest\>(). Далее следует разработка Fluent API. Класс MappingExpression должен сохранять пары типов, а метод ForMember — сохранять выражения источника, ассоциированные со свойствами назначения, во внутреннюю структуру метаданных (например, в коллекцию List\<PropertyMap\>). Обязательным элементом является реализация соглашения о маппинге по умолчанию (Convention-based mapping). Рефлексия должна автоматически находить совпадающие по имени свойства между TSource и TDest, генерируя для них маппинг Expression.Property(), если пользователь явно не переопределил их поведение через ForMember.19

### **Этап 2: Компилятор выражений (Expression Builder Engine)**

Этот этап требует глубоких знаний платформы.NET. Разрабатывается класс-строитель, который на основе данных из TypeMap конструирует узел Expression.New для инициализации целевого объекта. Для каждого свойства создается Expression.Bind, который связывает свойство целевого объекта с лямбда-выражением источника.23 Ключевым компонентом здесь является ParameterReplaceVisitor, который берет разрозненные пользовательские выражения из ForMember, извлекает из них тела и перенаправляет все локальные параметры на один глобальный ParameterExpression, представляющий корень запроса к базе данных.31

### **Этап 3: Система динамических контекстных параметров**

На этом этапе реализуется требование по работе со справочными таблицами и локализацией. Разрабатывается маркерный интерфейс IMappingContext с методами извлечения значений, например, GetValue\<T\>(string key). В Profile добавляется перегрузка метода MapFrom, принимающая контекст. Затем реализуется ядро трансформации реального времени: ParameterInjectionVisitor. Этот класс сканирует сформированное дерево на наличие обращений к IMappingContext, сопоставляет их с анонимным объектом словаря параметров, переданным пользователем во время запроса, и заменяет узлы вызова методов на Expression.Constant.30

### **Этап 4: Интеграция с EF Core и создание методов расширения**

Разрабатывается статический класс QueryableExtensions, содержащий метод ProjectTo\<TDest\>(this IQueryable source, IMapper mapper, object parameters \= null). Метод должен получать закэшированное дерево выражений из экземпляра IMapper (полученного из DI как Scoped-сервис), прогонять его через инжектор параметров, оборачивать в вызов Queryable.Select с помощью Expression.Call и возвращать новый экземпляр провайдера IQueryable.9 Проводятся интеграционные тесты с библиотекой EntityFrameworkCore.Projectables для подтверждения того, что вызовы \[Projectable\] методов корректно транслируются встроенным препроцессором EF Core.10

### **Этап 5: Управление жизненным циклом и Dependency Injection**

Финальный этап включает создание методов расширения для Microsoft.Extensions.DependencyInjection. Разрабатывается логика автоматического сканирования загруженных сборок (AppDomain.CurrentDomain.GetAssemblies()) для выявления всех пользовательских реализаций Profile.2 Экземпляры профилей создаются, инициируют процесс генерации всех базовых деревьев выражений, после чего результаты помещаются в потокобезопасный ConcurrentDictionary конфигурации. Объект конфигурации регистрируется в контейнере как AddSingleton(), что удовлетворяет требованию оптимального управления памятью. Сам исполнительный сервис IMapper, отвечающий за передачу контекстных параметров, регистрируется как AddScoped().7

Соблюдение данного архитектурного регламента позволит создать надежный, отказоустойчивый и высокопроизводительный инструмент, превосходящий функциональность AutoMapper в специфических задачах проецирования EF Core, полностью удовлетворяя всем техническим требованиям.

#### **Источники**

1. AutoMapper Documentation, дата последнего обращения: марта 21, 2026, [https://docs.automapper.org/\_/downloads/en/v12.0.1/pdf/](https://docs.automapper.org/_/downloads/en/v12.0.1/pdf/)  
2. AutoMapper in .NET: Beyond the Basics | by Afroz | Medium, дата последнего обращения: марта 21, 2026, [https://medium.com/@afrozmohd/automapper-in-net-beyond-the-basics-ae08ec290978](https://medium.com/@afrozmohd/automapper-in-net-beyond-the-basics-ae08ec290978)  
3. How to use AutoMapper with IQueryable in .NET Development \- Medium, дата последнего обращения: марта 21, 2026, [https://medium.com/@c.charalambos1998/how-to-use-automapper-with-iqueryable-in-net-development-354a0bb37cbe](https://medium.com/@c.charalambos1998/how-to-use-automapper-with-iqueryable-in-net-development-354a0bb37cbe)  
4. Configuration \- AutoMapper documentation, дата последнего обращения: марта 21, 2026, [https://docs.automapper.io/en/stable/Configuration.html](https://docs.automapper.io/en/stable/Configuration.html)  
5. How to Use AutoMapper in .NET Applications \- OneUptime, дата последнего обращения: марта 21, 2026, [https://oneuptime.com/blog/post/2026-01-27-dotnet-automapper/view](https://oneuptime.com/blog/post/2026-01-27-dotnet-automapper/view)  
6. AutoMapper Usage Guidelines \- Jimmy Bogard, дата последнего обращения: марта 21, 2026, [https://www.jimmybogard.com/automapper-usage-guidelines/](https://www.jimmybogard.com/automapper-usage-guidelines/)  
7. Understanding Singleton, Scoped, and Transient in .NET Core | by Susitha Bandara, дата последнего обращения: марта 21, 2026, [https://medium.com/@susithapb/understanding-singleton-scoped-and-transient-in-net-core-b7efede6c513](https://medium.com/@susithapb/understanding-singleton-scoped-and-transient-in-net-core-b7efede6c513)  
8. Custom Value Resolvers \- AutoMapper documentation, дата последнего обращения: марта 21, 2026, [https://docs.automapper.io/en/stable/Custom-value-resolvers.html](https://docs.automapper.io/en/stable/Custom-value-resolvers.html)  
9. Queryable Extensions — AutoMapper documentation, дата последнего обращения: марта 21, 2026, [https://docs.automapper.io/en/stable/Queryable-Extensions.html](https://docs.automapper.io/en/stable/Queryable-Extensions.html)  
10. EntityFrameworkCore.Projectables/README.md at master ... \- GitHub, дата последнего обращения: марта 21, 2026, [https://github.com/koenbeuk/EntityFrameworkCore.Projectables/blob/master/README.md](https://github.com/koenbeuk/EntityFrameworkCore.Projectables/blob/master/README.md)  
11. AutoMapper in C\#. A Practical Guide (with Patterns… | by Vikas Jindal \- Medium, дата последнего обращения: марта 21, 2026, [https://medium.com/@vikkasjindal/automapper-in-c-7bcf3b24c601](https://medium.com/@vikkasjindal/automapper-in-c-7bcf3b24c601)  
12. Expression Trees \- C\# | Microsoft Learn, дата последнего обращения: марта 21, 2026, [https://learn.microsoft.com/en-us/dotnet/csharp/advanced-topics/expression-trees/](https://learn.microsoft.com/en-us/dotnet/csharp/advanced-topics/expression-trees/)  
13. Working with Expression Trees in C\# • Oleksii Holub, дата последнего обращения: марта 21, 2026, [https://tyrrrz.me/blog/expression-trees](https://tyrrrz.me/blog/expression-trees)  
14. The power of Entity Framework Core and LINQ Expression Tree combined | by Erick Gallani, дата последнего обращения: марта 21, 2026, [https://medium.com/@erickgallani/the-power-of-entity-framework-core-and-linq-expression-tree-combined-6b0d72cf41db](https://medium.com/@erickgallani/the-power-of-entity-framework-core-and-linq-expression-tree-combined-6b0d72cf41db)  
15. Expression and Projection Magic for Entity Framework Core \- Ben Cull, дата последнего обращения: марта 21, 2026, [https://bencull.com/blog/expression-projection-magic-entity-framework-core](https://bencull.com/blog/expression-projection-magic-entity-framework-core)  
16. Entity Framework projections to Immutable Types (IEnumerable vs IQueryable), дата последнего обращения: марта 21, 2026, [https://www.productiverage.com/entity-framework-projections-to-immutable-types-ienumerable-vs-iqueryable](https://www.productiverage.com/entity-framework-projections-to-immutable-types-ienumerable-vs-iqueryable)  
17. Query Optimization in Entity Framework Core: Strategies for High-Performance Data Retrieval | by Mostafa Elnady | Medium, дата последнего обращения: марта 21, 2026, [https://medium.com/@mostafaelnady1997/query-optimization-in-entity-framework-core-strategies-for-high-performance-data-retrieval-55a27d38b279](https://medium.com/@mostafaelnady1997/query-optimization-in-entity-framework-core-strategies-for-high-performance-data-retrieval-55a27d38b279)  
18. Client vs. Server Evaluation \- EF Core | Microsoft Learn, дата последнего обращения: марта 21, 2026, [https://learn.microsoft.com/en-us/ef/core/querying/client-eval](https://learn.microsoft.com/en-us/ef/core/querying/client-eval)  
19. AutoMapper — AutoMapper documentation, дата последнего обращения: марта 21, 2026, [https://docs.automapper.io/](https://docs.automapper.io/)  
20. How to use AutoMapper .ForMember? \- Codemia, дата последнего обращения: марта 21, 2026, [https://codemia.io/knowledge-hub/path/how\_to\_use\_automapper\_formember](https://codemia.io/knowledge-hub/path/how_to_use_automapper_formember)  
21. How to use AutoMapper .ForMember? \- Stack Overflow, дата последнего обращения: марта 21, 2026, [https://stackoverflow.com/questions/6985000/how-to-use-automapper-formember](https://stackoverflow.com/questions/6985000/how-to-use-automapper-formember)  
22. AutoMapper's ForMember \- GeoffHudik.com, дата последнего обращения: марта 21, 2026, [https://geoffhudik.com/tech/2012/08/03/automappers-formember-html/](https://geoffhudik.com/tech/2012/08/03/automappers-formember-html/)  
23. ConstructProjectionUsing \- what am I doing wrong? \- Stack Overflow, дата последнего обращения: марта 21, 2026, [https://stackoverflow.com/questions/36898275/constructprojectionusing-what-am-i-doing-wrong](https://stackoverflow.com/questions/36898275/constructprojectionusing-what-am-i-doing-wrong)  
24. Create automapper configuration using expression tree \- Stack Overflow, дата последнего обращения: марта 21, 2026, [https://stackoverflow.com/questions/60761782/create-automapper-configuration-using-expression-tree](https://stackoverflow.com/questions/60761782/create-automapper-configuration-using-expression-tree)  
25. Using Scoped Services From Singletons in ASP.NET Core, дата последнего обращения: марта 21, 2026, [https://www.milanjovanovic.tech/blog/using-scoped-services-from-singletons-in-aspnetcore](https://www.milanjovanovic.tech/blog/using-scoped-services-from-singletons-in-aspnetcore)  
26. The Right Way To Use Scoped Services From Singletons in ASP.NET Core \- YouTube, дата последнего обращения: марта 21, 2026, [https://www.youtube.com/watch?v=FSjCGdkbiCA](https://www.youtube.com/watch?v=FSjCGdkbiCA)  
27. Using a Scoped service in a Singleton in an Asp.Net Core app \- Stack Overflow, дата последнего обращения: марта 21, 2026, [https://stackoverflow.com/questions/55708488/using-a-scoped-service-in-a-singleton-in-an-asp-net-core-app](https://stackoverflow.com/questions/55708488/using-a-scoped-service-in-a-singleton-in-an-asp-net-core-app)  
28. Passing Parameters with Automapper \- Code Buckets, дата последнего обращения: марта 21, 2026, [https://codebuckets.com/2016/09/24/passing-parameters-with-automapper/](https://codebuckets.com/2016/09/24/passing-parameters-with-automapper/)  
29. Simple way to add extra parameter to Automapper ForMember \- Stack Overflow, дата последнего обращения: марта 21, 2026, [https://stackoverflow.com/questions/67788867/simple-way-to-add-extra-parameter-to-automapper-formember](https://stackoverflow.com/questions/67788867/simple-way-to-add-extra-parameter-to-automapper-formember)  
30. ReplacingExpressionVisitor Class (Microsoft.EntityFrameworkCore.Query), дата последнего обращения: марта 21, 2026, [https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.query.replacingexpressionvisitor?view=efcore-10.0](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.query.replacingexpressionvisitor?view=efcore-10.0)  
31. c\# \- Expression Visitor to be used on other type \- Stack Overflow, дата последнего обращения: марта 21, 2026, [https://stackoverflow.com/questions/75162172/expression-visitor-to-be-used-on-other-type](https://stackoverflow.com/questions/75162172/expression-visitor-to-be-used-on-other-type)  
32. Expression Trees in C\#: Practical Guide | by Anthony Gregori \- Medium, дата последнего обращения: марта 21, 2026, [https://medium.com/@anthony.gregori78200/expression-trees-in-c-practical-guide-4cb233447328](https://medium.com/@anthony.gregori78200/expression-trees-in-c-practical-guide-4cb233447328)  
33. Expression Visitor access property on parameter representing local variable, дата последнего обращения: марта 21, 2026, [https://stackoverflow.com/questions/78728040/expression-visitor-access-property-on-parameter-representing-local-variable](https://stackoverflow.com/questions/78728040/expression-visitor-access-property-on-parameter-representing-local-variable)  
34. Replace parameter value in Expression Tree \- Stack Overflow, дата последнего обращения: марта 21, 2026, [https://stackoverflow.com/questions/15908669/replace-parameter-value-in-expression-tree](https://stackoverflow.com/questions/15908669/replace-parameter-value-in-expression-tree)  
35. How to Build a Query Builder with Expression Trees in .NET \- OneUptime, дата последнего обращения: марта 21, 2026, [https://oneuptime.com/blog/post/2026-01-25-query-builder-expression-trees-dotnet/view](https://oneuptime.com/blog/post/2026-01-25-query-builder-expression-trees-dotnet/view)  
36. Custom DTO Mapping in .NET Core: A Complete Guide | by Mina Golzari Dalir \- Medium, дата последнего обращения: марта 21, 2026, [https://medium.com/@minagolzaridalir/custom-dto-mapping-in-net-core-a-complete-guide-89e7d8f9224a](https://medium.com/@minagolzaridalir/custom-dto-mapping-in-net-core-a-complete-guide-89e7d8f9224a)  
37. Using a LINQ ExpressionVisitor to replace primitive parameters with property references in a lambda expression \- Stack Overflow, дата последнего обращения: марта 21, 2026, [https://stackoverflow.com/questions/11164009/using-a-linq-expressionvisitor-to-replace-primitive-parameters-with-property-ref](https://stackoverflow.com/questions/11164009/using-a-linq-expressionvisitor-to-replace-primitive-parameters-with-property-ref)  
38. AutoMapper and mapping Expressions \- Ahmadreza's blog, дата последнего обращения: марта 21, 2026, [https://ahmadreza.com/2014/09/automapper-and-mapping-expressions/](https://ahmadreza.com/2014/09/automapper-and-mapping-expressions/)  
39. How can I replace a type parameter in an expression tree? \- Stack Overflow, дата последнего обращения: марта 21, 2026, [https://stackoverflow.com/questions/39377090/how-can-i-replace-a-type-parameter-in-an-expression-tree](https://stackoverflow.com/questions/39377090/how-can-i-replace-a-type-parameter-in-an-expression-tree)  
40. Using LINQ Expression Trees to build maintainable DTO modeling \- Medium, дата последнего обращения: марта 21, 2026, [https://medium.com/@ryeenglish9/using-linq-expression-trees-to-build-maintainable-dto-modeling-85f06721099b](https://medium.com/@ryeenglish9/using-linq-expression-trees-to-build-maintainable-dto-modeling-85f06721099b)  
41. koenbeuk/EntityFrameworkCore.Projectables: Project over properties and functions in your linq queries \- GitHub, дата последнего обращения: марта 21, 2026, [https://github.com/koenbeuk/EntityFrameworkCore.Projectables](https://github.com/koenbeuk/EntityFrameworkCore.Projectables)  
42. EFCore.Projectables and the curious case of an unexpected performance boost, дата последнего обращения: марта 21, 2026, [https://onthedrift.com/posts/efcore-projectables-perf/](https://onthedrift.com/posts/efcore-projectables-perf/)  
43. Parameterized Query from an Expression Tree in Entity Framework Core \- Stack Overflow, дата последнего обращения: марта 21, 2026, [https://stackoverflow.com/questions/60515202/parameterized-query-from-an-expression-tree-in-entity-framework-core](https://stackoverflow.com/questions/60515202/parameterized-query-from-an-expression-tree-in-entity-framework-core)  
44. asp.net mvc \- Where to place AutoMapper.CreateMaps? \- Stack Overflow, дата последнего обращения: марта 21, 2026, [https://stackoverflow.com/questions/6825244/where-to-place-automapper-createmaps](https://stackoverflow.com/questions/6825244/where-to-place-automapper-createmaps)

[image1]: <data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAADAAAAAXCAYAAABNq8wJAAACx0lEQVR4Xu2XzauPQRTHj5e8k5eUi4RSuiykiEJXXjeKklKE6BIpb4kkhYWdEhJJJEJWVlaslAUL2dkoS38E308zxzPP/OZ53O79KVe+9eneOzPPzJwzZ86ca/Zf/5bGin1idt4xBK0Tq/LGNo2ILBHXxAPxWDwVG8TIamhNo8VpsTbviNomzkf2WjDW1RM5mYw5KiZZmPeY6I20aoq4GrkoJiZ9c8VLsccqI1P1ibNiVNbuYjOrxXvxRaxM+tgkcHK3xS4xwypnsfbNyMzY1iEW4GP3ABPm2iI+iaURF9/eytpK2iwOinfiinUaO0GcEpOzdpx1JsLpFTWsDWCzbJoQmR4pabH4IHZHXCvEfetcOBWbIK7nWdg8RiysjTCbL45bZ3gi1oC7FhxWE7f8q9iZtedyA/BC6okj4kLyd0kYd8KCl329Q+kAC/eIUy6J2AeSyaK0Y4y4bmWP5OoTP6xuAGFAtioebSKM9zF4kOzmJ+7qtzCuJAyHe2JN2oFVr2MHA9pEfH63kE0A8c0dyyYtiPhPUyynzVybrNpcKf5dPoa1as7ysLhs5dhz4Sk89lxMi6CBGODxnz5w/I7jbljYAzTFP2o0gHgiN19KGwvaKr7Fn6kGYkAa/y42ethCVvPM1xT/qNGAqeJF7GgKoR7xysrp1SfdnrWnwrvEdy7uHHcPB0LtcmZKDegwdIf4KJblHRY2/9DCyzw+63NxevvzRqtebF5WypBcJABS6qNIU/wjT++EMem0Jrx6QLyxKsevt1CbPLP2GgjhfTJR+jD1ibcRLutnC/ds3K8RQaRUShBoE6cD1GWN5QRHtDxCFTjH2jfuIhSeWHer0Fw4CYiEpos+/A0YrAgdLnieobolSm9CFH5Xbw1asyws4O9DN7XRwhvR9k50Rb0W/vnIU+1QtECcs1B+dBRxf0JkmW4a0O35/i79BNwvcffQTi/FAAAAAElFTkSuQmCC>

[image2]: <data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAABIAAAAXCAYAAAAGAx/kAAABFklEQVR4Xu3SsWoCQRAG4NEIkZA0WplCgiDkCUIsBEHUyk7srAwGhECSIpA2eQMLW8HeB7CzEtIGOxt7X8L/v53lJueFKyy9Hz6QuXH3bnZFziZd9akGcGmel+BN+Z4xXJueICxQDX5gCw/meQ5u1RT6UISs6fmTNgxhDd9wYZ5dqXe4MfXYnLxQRvG7y+IW4WIV03OnXsT1xoY70Ku4XR9hB0+mp6E6pnaUe8XTYjj4GSygoLWRYt+/4Wyobmo92ENLwtkkzoezIR6vD38vYSLuLTibxPlwNn4+PvzDM/yKu4CcTeJ8/PdHw1Pj6fGSVlVsuCtvaVNFw3vEqzCX8GSP0oCVuIFu1Bfkw5YgvAofkVqaNMwBOhIo/cVquf8AAAAASUVORK5CYII=>

[image3]: <data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAC0AAAAYCAYAAABurXSEAAACmElEQVR4Xu2X34tNURTHFybC+DEm+fmAIsY8SCkKHcmPlEIkvzLzMKWUmmlepKEkvMibhIiU0NTIgxR5UOKBB69elAcP/gi+37vXunfdfc8+nXvwcOVTn26z9zmndfZae509Iv/pTGbDIditVoX3ntTf0s+ZBNfCq/AefAgfwe1wshrDh1+Gq+IJMBUeg2cL3Atn2g2gD55Wu9x4ko4Kmumll+CYND9kKRyHx1W+mOeEmgevnQP3wJ/wAOx1roTX4QRc6O5hqdGjOtYCV+qGyjfPe7td8LPa78YXwzv6W8Qp+BYui8bJGvhJwjXGCvUuXODGazBABsqVpPOap+ushh/VI258n4RSmuLGYlgiXM3bcEY0R7bAH9IcNO+h1+AON15jI/wKD6opfNBWCgyUAfuXyGM+fAlH4gkJpTAK30n+nhiA5/yArQBvsHSkyCTUJLWgZ8EHcLP+nWKDhIXJmodrm3onfC+tc0YGb4rLkK2ApS0vdQZXiSmkm3SMG4mdhVkoYkBC0PclBGCyM52R8JwUXJAX4q6xlF+UkKa4Kxisc9b7E7VHx8sEbdlkRpiZdvk3gmaP/AAv2ECC3fCb/lKjTNBsha8l2kxt0BL0XPhUGoWeV9OL4HNp9G/fw/mgZxI2WgrbhOzzVWDQLVnaL6Gxr1M9DJibh1/I6dEc4UvyhbNo3MPey2wyq1VgO2V5sczqcOUG4RuVF22Dw/CxNM4cKdhV/EfBOCShM32B3+ErCWeJdmFZpY4I9fJYD7fCJVIcrMGP0y1p4xhZEpYu5RGhaM9Ugi/K9MWl9btwMSibRNERoTI8Sp6H0+KJijBrV9Tl0dwfoyODJhk8LMUfqTKwMfDfrUz967C+uXnLbOAUvDfve9G5/ALAwnmJb53AUwAAAABJRU5ErkJggg==>

[image4]: <data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAA8AAAAYCAYAAAAlBadpAAAA00lEQVR4XmNgGPKADYijobgSC86BYg0gZoTqgQOKNIME+KE4HoiPAbEBEAtDsQsUHwXiGKh6rKAaiBcBMS+6BBDUA/FmIBZCl+CC4tlAXMuAajoHFE9jwKFZAYoPA3EAigwDgxIUg7xTyIDF2bZQfBqINZHEuYG4CYonAjEPkhwcZEDxFSCeC8QzGCBemAPE7lDMDleNBGD+AeF+BkjUEQ0o0iwFxHuhOAFVijAwZYD4FYRBbJJAAgPCZpAriAIOQLwTiG8D8XMo3gPEvkhqRsHQBgAbFSlEBBQPwwAAAABJRU5ErkJggg==>

[image5]: <data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACgAAAAYCAYAAACIhL/AAAACN0lEQVR4Xu2Wz0tVQRiGvzKiMiwNqayFBIFpULRSyFAibKkUaFSQ0K6V6UYijGgRtmgTEbiwIoxMV65c5UpwYYu2bYKW/hH2vs47986Ze+Z41YQr+MAD15lzznzz4/tGs31qiw55J+4ooAUOwUNxRxEHzA30Gk7DL/ArvCkPlh8t0Q5fyWNRH2EAp2VMDxw0Ny7dlJoNsEFykOewPug7D+flA8t+iAG9gW3Sw6DOwNtwBk7IGD73AnbLXI7D93Lc8s9En/wFLwftt+BLWCc9Z+Fjc6v+2dIBkk74TlbsAINhUH6FmrLdJfwKrcJ7ajsM35oLPAUH/GDFAZ6EH2U4+Q0Y/R94V6YIA3yoNmbhd3jRP5RDNQHyyHAXqJ/8Bn4FluEFmaJHrls5wCtwDp7S33lUEyB5JDPPNMNFOGXuQxX7H/BUrsEutV03N3jRe9UGyEnTzPf8lnFpi9Kc59Kf0VnYqPb9AHm4V6z4RcJa9lfyt2fXA2R6Mwt9Y95ArGcL5kpRXCMZIN/nd1JsNUDeYGE9tQH4E16VIQzuk7mb5agM4RH5Zi7ZUlQb4DPJRMzAFRmGPyTrUC8cMTd46v4lXDlOgOUmhueU1WHJXOb/lqwavE6PlJ50v/1Nkrzu/BZfgzfgOUsH5mFSjZm70nZCq7l/TGjRbmwLbjNnfkJuB95gnOROJ5oLV/E+7Jdbhed80tyR8OXrv1LzARIm2hPZHvUVwaowCi/FHbsBE4rm1dIUnFiYzXuLf48CcUeJ0tEBAAAAAElFTkSuQmCC>

[image6]: <data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAC4AAAAXCAYAAAB0zH1SAAACr0lEQVR4Xu2W28tNQRjGH4fI+ZQci+RMkVKUtERxo4iSogglLhwuFIVCkhK5QIjIIaciF7gQV8oFF/IHfOXSH8Hz7HnHmjVrZn277fvKLr/6tfeeWXv2O+96590L+E93M5ruoyPjiQ7YROfGg00MMBfRC/QufUAf07V0YHlpBQV7Hvkf20iPt+EBuLXkMTrZbEQZO2eepCOCuen0Bd2BcnMhO80UunYMXUm/0cN0QuRCuETdosPd17CUXjT9WA3t8BrKnQ+uTrdYD/fDi03PVHrbXpvQ93vo8mjco++foEPs81B6ySxsrEZXBq4gFaxKYbyZYj79QrebHh0k3eZBwViMykXl9x7lBnW9nGHzGj9ocx6tLc8isf4KuExsjcZjfOBhPWsxBR1uJMUoep9eRpnRWeZe+6xan2fvPQvMh4gSqkW02Ce4RZoo6C9UA/cBrbLPOfymr9A1cF3mkakSyjHRfAK3RmXiHaqnOcdR+hOuO0ihLKlVVhZNoNvdQ/fABb4FrjTlzD9X1fFd5w2i5PhMnEG9xYXoNulHntJxpmgncF/fSpASJZQktUWpuyZ0aCfZe0828Dn0Mz0dDibYQH/Ya0g7gY+lz1Ctb3WM2aZQIg7ZeEg2cL/oDeRLZQp9jXSb1KKvkG9xwt/VXdF4iEppXTyIMvDndEk0h830K9w/VYyCvgd3q4dFc0Kb1aaLaDzE13e8OT0+SB3OO0i3YVWEfInEWVAWd9MPKHu0DtARuNPc9IwidGj3R2OF+ZZ+hzvUH+2zV+NSncq3xBjfCK4jXxGtiWXmajoNzQF79D9wE33zVBijpMjcc1CLrg28U7RhdYzUGfkbVPNXzd6egzpGj6WnUG9nnaLeryyrcch+paDb0PxH1i7qQHrgUuOIW3C/oLJp51z0Rl+t82/xG2dddeYVoABRAAAAAElFTkSuQmCC>

[image7]: <data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAA8AAAAZCAYAAADuWXTMAAABAUlEQVR4Xu3RvUtCYRQG8GOBhqCgomg6SBC4OriIgk0iLq4SDUGgDiI5CA0iOLk3hZt7c7pJTq2ujo7+Ez3nvo94r59Qk9QDP+59P865L+8V+dvxwAO9nPAMMVNmcgEBqsEcshCiKN3DJ6RM2SYu6sMIfM5lK27oiGnoiG5WWtgV00gTBj9dQZP7HLmlL6hwThs8wjVdQoZPR35VXKQlPMEdNOAVvHQwbfqAspjiHueORrsOaSCbY+WgwHdNHNK2sZUkzKhqm0+IueX1b9S1G9u6lTwsSC9kO9pEtWTPZdVhQvpf7YnAG+lHdvKj4pKYgvWRlY7HNIUVvFPQqvrPOeUbo9IuYZPk5g8AAAAASUVORK5CYII=>