# 🎬 CineNova — Enterprise Web API with Vector Search & Analytics

<div align="center">

![C#](https://img.shields.io/badge/C%23-239120?style=for-the-badge&logo=c-sharp&logoColor=white)
![.NET 8](https://img.shields.io/badge/.NET_8-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-pgvector-336791?style=for-the-badge&logo=postgresql&logoColor=white)
![Docker](https://img.shields.io/badge/Docker-Containerized-2496ED?style=for-the-badge&logo=docker&logoColor=white)
![Swagger](https://img.shields.io/badge/Swagger-OpenAPI-85EA2D?style=for-the-badge&logo=swagger&logoColor=black)

<p align="center">
  <strong>Production-ready RESTful Web API engineered with ASP.NET Core, featuring vector embeddings for semantic similarity, operational logging, real-time analytics views, and Docker containerization.</strong>
</p>

</div>

---

## 📋 Overview

**CineNova** is an enterprise-grade backend service built to power modern film cataloging, semantic recommendations, and user engagement. It combines traditional relational database modeling with high-performance vector search (`pgvector`) powered by **Google Cloud Vertex AI embeddings (768 dimensions)** to provide intelligent movie matching beyond standard keyword filtering.

The application incorporates strict architectural separation of concerns (Controllers, Services, DTOs, Custom Middleware, and Database Context), centralized exception handling, transaction consistency, and automated schema migration with SQL analytical views.

---

## 🏛️ Architecture & Highlights

```
┌─────────────────────────────────────────────────────────────┐
│                    CineNova Web Client                      │
└──────────────────────────────┬──────────────────────────────┘
                               │ HTTP / JSON (REST)
                               ▼
┌─────────────────────────────────────────────────────────────┐
│                   ASP.NET Core Web API                      │
│ ┌─────────────────────────────────────────────────────────┐ │
│ │ Middleware: Auth, Error Handling, Latency & Audit Logs  │ │
│ ├─────────────────────────────────────────────────────────┤ │
│ │ Controllers: Auth, Movies, Agent (AI), Reviews, Admin   │ │
│ ├─────────────────────────────────────────────────────────┤ │
│ │ Services & DTOs: Ingestion, Validation, Semantic Search │ │
│ └────────────────────────────┬────────────────────────────┘ │
└──────────────────────────────┼──────────────────────────────┘
                               │
                ┌──────────────┴──────────────┐
                ▼                             ▼
   ┌───────────────────────────┐ ┌───────────────────────────┐
   │    PostgreSQL Database    │ │   Vertex AI Embeddings    │
   │  • pgvector (768-dim)     │ │   • Semantic Vectorizer   │
   │  • Analytical SQL Views   │ │   • AI Agent Processing   │
   │  • Audit & Latency Logs   │ └───────────────────────────┘
   └───────────────────────────┘
```

### Key Technical Capabilities:
- **Semantic Vector Search:** Leverages `pgvector` in PostgreSQL to store and query 768-dimensional embeddings for intelligent content matching.
- **Observability & Analytics:** Custom database views (`v_dashboard_summary`, `v_movies_per_user`, `v_errors_by_action`) and structured operation logs capturing latency metrics in milliseconds.
- **Security & Validation:** Role-based profile management (`client`, `admin`), email verification workflows, and database check constraints.
- **Cloud Ready:** Packaged with a production-optimized `Dockerfile` and `render.yaml` for automated zero-downtime deployment.

---

## 🛠️ Technology Stack

| Layer | Technologies |
|---|---|
| **Language & Framework** | C# 12, .NET 8 (ASP.NET Core Web API) |
| **Database & ORM** | PostgreSQL with `pgvector`, Entity Framework Core |
| **Cloud & Deployment** | Docker, Render Cloud Platform (`render.yaml`) |
| **Documentation & Tooling** | Swagger / OpenAPI UI, REST Client HTTP specs |

---

## 🔌 API Endpoints (Core Modules)

| Module | Method | Endpoint | Description |
|---|:---:|---|---|
| **Auth** | `POST` | `/api/Auth/register` | User profile registration with credential hashing |
| **Auth** | `POST` | `/api/Auth/login` | Authentication and session token validation |
| **Movies** | `GET` | `/api/Movies` | Paginated movie catalog retrieval |
| **Movies** | `POST` | `/api/Movies` | Ingest film metadata and generate vector embeddings |
| **AI Agent** | `POST` | `/api/Agent/query` | Vector similarity search across movie embeddings |
| **Reviews** | `POST` | `/api/MovieReviews` | Submit movie rating with constraint validation |
| **Dashboard** | `GET` | `/api/Dashboard/summary` | Query analytical metrics, error rates & latency |

---

## 🚀 Getting Started

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [PostgreSQL](https://www.postgresql.org/) with `pgvector` extension enabled
- [Docker](https://www.docker.com/) (optional, for containerized execution)

### 1. Clone the repository
```bash
git clone https://github.com/AlexEspinoza2005/CineNova_1-.git
cd CineNova_1-
```

### 2. Configure Database Connection
Update `appsettings.json` with your PostgreSQL credentials:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=cinenova_db;Username=postgres;Password=your_password;"
  }
}
```

### 3. Run with .NET CLI
```bash
dotnet restore
dotnet build
dotnet run
```
Navigate to `http://localhost:5000/swagger` to explore interactive API documentation.

### 4. Run with Docker
```bash
docker build -t cinenova-api .
docker run -p 8080:8080 cinenova-api
```

---

## 👨‍💻 Author

**Alex Espinoza**  
Software Engineering Student · Imbabura, Ecuador  
- LinkedIn: [Alex Espinoza](https://www.linkedin.com/in/alex-anthony-espinoza-cang%C3%A1s-53530824b/)  
- GitHub: [@AlexEspinoza2005](https://github.com/AlexEspinoza2005)  
- Email: [alexespinozacangas2018@gmail.com](mailto:alexespinozacangas2018@gmail.com)
