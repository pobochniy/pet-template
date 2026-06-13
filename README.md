# PetTemplate

Full-stack pet project template: ASP.NET Core API + Angular frontend.

## Stack

- **Backend:** ASP.NET Core (.NET 10), MySQL
- **Frontend:** Angular
- **Infrastructure:** Docker Compose

## Usage

### Install template from GitHub

```bash
dotnet new install https://github.com/pobochniy/pet-template
```

### Create new project

```bash
dotnet new pet -n MyAwesomeProject
```

This will create a new folder `MyAwesomeProject/` with all `PetTemplate` references replaced by `MyAwesomeProject`.

### Update template

After pushing changes to the repository, reinstall the template with `--force` to overwrite the local copy:

```bash
dotnet new install https://github.com/pobochniy/pet-template --force
```

### Uninstall template

```bash
dotnet new uninstall https://github.com/pobochniy/pet-template
```

## Local development

### Install template locally

```bash
dotnet new install ./
```

### Run infrastructure

```bash
docker-compose up -d
```
